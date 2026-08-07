using Epimeteo.Server.Content;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;

namespace Epimeteo.Server.Inventory;

/// <summary>Un hueco (contenedor + posición) que cambió tras una mutación.</summary>
public readonly record struct SlotRef(ContainerId Container, byte Slot);

/// <summary>Resultado de una mutación de inventario. Nunca lanza (CLAUDE.md §4).</summary>
public readonly record struct InventoryOpResult(bool Ok, ResultCode Code, IReadOnlyList<SlotRef> Touched)
{
    public static InventoryOpResult Fail(ResultCode code) => new(false, code, []);

    public static InventoryOpResult Success(params SlotRef[] touched) => new(true, ResultCode.Ok, touched);

    /// <summary>Éxito que en la práctica no movió nada (ej. apilar contra un slot ya al máximo).</summary>
    public static InventoryOpResult NoOp => new(true, ResultCode.Ok, []);
}

/// <summary>Como <see cref="InventoryOpResult"/>, con la curación aplicada por <c>InvUse</c> (FASE-06 §2 D5).</summary>
public readonly record struct InventoryUseResult(bool Ok, ResultCode Code, IReadOnlyList<SlotRef> Touched, int HealAmount)
{
    public static InventoryUseResult Fail(ResultCode code) => new(false, code, [], 0);
}

/// <summary>
/// Stats derivados de base + equipo (FASE-06 §2 D5). Desde la Fase 9 incluye ataque y defensa,
/// que la Fase 6 dejó explícitamente fuera por no haber contra qué calcularlos todavía.
/// </summary>
public readonly record struct DerivedStats(
    int HpMax, int MpMax, int StrEffective, int IntEffective, int VitEffective, int DexEffective,
    int Attack, int Defense);

/// <summary>
/// Mover, apilar, dividir, tirar, usar, equipar y desequipar — puro dado un
/// <see cref="PlayerInventory"/> y un <see cref="ItemCatalog"/>, sin I/O, para que el tick lo
/// ejecute sin tocar Postgres (CLAUDE.md §4) y para que se pueda probar sin servidor ni BD.
/// Mismo espíritu que <c>Shared/Simulation/MovementSystem</c>, aunque este vive en <c>Server</c>
/// porque el inventario no necesita predicción de cliente (a diferencia del movimiento).
/// </summary>
public static class InventorySystem
{
    /// <summary>
    /// Mover, apilar o dividir entre dos huecos de bolsa (0/1/2). <see cref="ContainerId.Equipped"/>
    /// no se toca aquí — usa <see cref="TryEquip"/>/<see cref="TryUnequip"/> (FASE-06 §2 D4).
    /// </summary>
    public static InventoryOpResult TryMove(
        PlayerInventory inventory, ItemCatalog catalog,
        ContainerId fromContainer, byte fromSlot, ContainerId toContainer, byte toSlot, int quantity)
    {
        if (fromContainer == ContainerId.Equipped || toContainer == ContainerId.Equipped)
        {
            return InventoryOpResult.Fail(ResultCode.NotEquippable);
        }

        var source = inventory.Find(fromContainer, fromSlot);
        if (source is null)
        {
            return InventoryOpResult.Fail(ResultCode.ItemNotFound);
        }

        if (quantity <= 0 || quantity > source.Quantity)
        {
            return InventoryOpResult.Fail(ResultCode.NotEnoughItems);
        }

        if (!catalog.TryGet(source.DefKey, out var sourceDef))
        {
            return InventoryOpResult.Fail(ResultCode.UnknownError);
        }

        if (InventoryConstants.AllowedContainer(sourceDef.Type) != toContainer)
        {
            return InventoryOpResult.Fail(ResultCode.NotEquippable);
        }

        if (fromContainer == toContainer && fromSlot == toSlot)
        {
            return InventoryOpResult.NoOp;
        }

        var target = inventory.Find(toContainer, toSlot);

        if (target is null)
        {
            return MoveToEmpty(inventory, source, toContainer, toSlot, quantity, fromContainer, fromSlot);
        }

        if (target.DefKey == source.DefKey && sourceDef.MaxStack > 1)
        {
            return StackOnto(inventory, source, target, quantity, fromContainer, fromSlot, toContainer, toSlot, sourceDef.MaxStack);
        }

        // Ítems distintos (o no apilables): intercambiar de sitio requiere que el que ya estaba
        // ahí encaje también en el contenedor de origen.
        if (!catalog.TryGet(target.DefKey, out var targetDef))
        {
            return InventoryOpResult.Fail(ResultCode.UnknownError);
        }

        if (InventoryConstants.AllowedContainer(targetDef.Type) != fromContainer)
        {
            return InventoryOpResult.Fail(ResultCode.NotEquippable);
        }

        (source.Container, source.Slot, target.Container, target.Slot) = (toContainer, toSlot, fromContainer, fromSlot);
        return InventoryOpResult.Success(new SlotRef(fromContainer, fromSlot), new SlotRef(toContainer, toSlot));
    }

    private static InventoryOpResult MoveToEmpty(
        PlayerInventory inventory, ItemStack source, ContainerId toContainer, byte toSlot, int quantity,
        ContainerId fromContainer, byte fromSlot)
    {
        if (quantity == source.Quantity)
        {
            source.Container = toContainer;
            source.Slot = toSlot;
            return InventoryOpResult.Success(new SlotRef(fromContainer, fromSlot), new SlotRef(toContainer, toSlot));
        }

        // Dividir: el resto se queda donde estaba, una parte nueva aparece en el destino.
        source.Quantity -= quantity;
        inventory.Add(new ItemStack
        {
            DefKey = source.DefKey,
            Container = toContainer,
            Slot = toSlot,
            Quantity = quantity,
            Durability = source.Durability,
            DurabilityMax = source.DurabilityMax,
            Quality = source.Quality,
            BoundTo = source.BoundTo,
        });

        return InventoryOpResult.Success(new SlotRef(fromContainer, fromSlot), new SlotRef(toContainer, toSlot));
    }

    private static InventoryOpResult StackOnto(
        PlayerInventory inventory, ItemStack source, ItemStack target, int quantity,
        ContainerId fromContainer, byte fromSlot, ContainerId toContainer, byte toSlot, int maxStack)
    {
        var room = maxStack - target.Quantity;
        var amount = Math.Min(quantity, room);

        if (amount <= 0)
        {
            // Ya está al máximo: no es un error (FASE-06 §5), simplemente no cambia nada.
            return InventoryOpResult.NoOp;
        }

        target.Quantity += amount;
        source.Quantity -= amount;

        if (source.Quantity == 0)
        {
            inventory.Remove(source);
        }

        return InventoryOpResult.Success(new SlotRef(fromContainer, fromSlot), new SlotRef(toContainer, toSlot));
    }

    /// <summary>Tirar (destruir) parte o todo un stack. Sin saco de loot: no deja nada en el mundo (FASE-06 §1).</summary>
    /// <summary>
    /// Añadir un stack que no viene de ningún hueco (comprado en una tienda, botín futuro): al
    /// contrario que <see cref="TryMove"/>, aquí no hay origen que vaciar. Apila primero contra
    /// lo que ya haya del mismo ítem, y sólo abre huecos nuevos con lo que sobre.
    /// <para>
    /// La capacidad se comprueba <b>antes</b> de tocar nada: si no cupiera todo a mitad de
    /// apilar, dejar unos stacks ya crecidos y fallar a medias rompería la garantía de "si falla,
    /// no cambia nada" que tienen el resto de operaciones (FASE-07 §5, `ShopSystem.TryBuy`).
    /// </para>
    /// </summary>
    public static InventoryOpResult TryAddNew(
        PlayerInventory inventory, ItemCatalog catalog, string defKey, int quantity,
        int? durability = null, int? durabilityMax = null, byte quality = 0)
    {
        if (quantity <= 0)
        {
            return InventoryOpResult.Fail(ResultCode.NotEnoughItems);
        }

        if (!catalog.TryGet(defKey, out var def))
        {
            return InventoryOpResult.Fail(ResultCode.UnknownError);
        }

        var container = InventoryConstants.AllowedContainer(def.Type);

        var capacity = 0;
        if (def.MaxStack > 1)
        {
            foreach (var stack in inventory.Stacks)
            {
                if (stack.Container == container && stack.DefKey == defKey)
                {
                    capacity += def.MaxStack - stack.Quantity;
                }
            }
        }

        var usedSlots = 0;
        foreach (var stack in inventory.Stacks)
        {
            if (stack.Container == container)
            {
                usedSlots++;
            }
        }

        capacity += (InventoryConstants.CapacityOf(container) - usedSlots) * def.MaxStack;

        if (capacity < quantity)
        {
            return InventoryOpResult.Fail(ResultCode.InventoryFull);
        }

        var remaining = quantity;
        var touched = new List<SlotRef>();

        if (def.MaxStack > 1)
        {
            foreach (var stack in inventory.Stacks)
            {
                if (remaining <= 0)
                {
                    break;
                }

                if (stack.Container != container || stack.DefKey != defKey)
                {
                    continue;
                }

                var room = def.MaxStack - stack.Quantity;
                if (room <= 0)
                {
                    continue;
                }

                var add = Math.Min(room, remaining);
                stack.Quantity += add;
                remaining -= add;
                touched.Add(new SlotRef(stack.Container, stack.Slot));
            }
        }

        while (remaining > 0)
        {
            // Ya se comprobó arriba que cabe entero: FindEmptySlot no puede fallar aquí.
            var freeSlot = inventory.FindEmptySlot(container)!.Value;
            var amount = Math.Min(remaining, def.MaxStack);

            inventory.Add(new ItemStack
            {
                DefKey = defKey,
                Container = container,
                Slot = freeSlot,
                Quantity = amount,
                Durability = durability,
                DurabilityMax = durabilityMax,
                Quality = quality,
            });

            touched.Add(new SlotRef(container, freeSlot));
            remaining -= amount;
        }

        return InventoryOpResult.Success([.. touched]);
    }

    public static InventoryOpResult TryDrop(PlayerInventory inventory, ContainerId container, byte slot, int quantity)
    {
        if (container == ContainerId.Equipped)
        {
            return InventoryOpResult.Fail(ResultCode.NotEquippable);
        }

        var stack = inventory.Find(container, slot);
        if (stack is null)
        {
            return InventoryOpResult.Fail(ResultCode.ItemNotFound);
        }

        if (quantity <= 0 || quantity > stack.Quantity)
        {
            return InventoryOpResult.Fail(ResultCode.NotEnoughItems);
        }

        stack.Quantity -= quantity;
        if (stack.Quantity == 0)
        {
            inventory.Remove(stack);
        }

        return InventoryOpResult.Success(new SlotRef(container, slot));
    }

    /// <summary>Usar un consumible de curación. Ninguna otra clase de ítem es "usable" todavía (FASE-06 §1).</summary>
    public static InventoryUseResult TryUse(PlayerInventory inventory, ItemCatalog catalog, ContainerId container, byte slot)
    {
        if (container == ContainerId.Equipped)
        {
            return InventoryUseResult.Fail(ResultCode.NotEquippable);
        }

        var stack = inventory.Find(container, slot);
        if (stack is null)
        {
            return InventoryUseResult.Fail(ResultCode.ItemNotFound);
        }

        if (!catalog.TryGet(stack.DefKey, out var def) || def.HealAmount <= 0)
        {
            return InventoryUseResult.Fail(ResultCode.NotEquippable);
        }

        stack.Quantity -= 1;
        if (stack.Quantity == 0)
        {
            inventory.Remove(stack);
        }

        return new InventoryUseResult(true, ResultCode.Ok, [new SlotRef(container, slot)], def.HealAmount);
    }

    /// <summary>
    /// Equipar un ítem de una bolsa. Si el hueco ya tenía algo, se intercambia: el ítem que había
    /// vuelve a su bolsa, y si no le cabe, el <c>Equip</c> entero se rechaza — nada se pierde en
    /// el limbo (FASE-06 §2 D4).
    /// </summary>
    public static InventoryOpResult TryEquip(
        PlayerInventory inventory, ItemCatalog catalog, ContainerId container, byte slot, EquipSlot equipSlot)
    {
        if (container == ContainerId.Equipped)
        {
            return InventoryOpResult.Fail(ResultCode.NotEquippable);
        }

        var source = inventory.Find(container, slot);
        if (source is null)
        {
            return InventoryOpResult.Fail(ResultCode.ItemNotFound);
        }

        if (!catalog.TryGet(source.DefKey, out var sourceDef) ||
            sourceDef.Type is not (ItemType.Weapon or ItemType.Armor) ||
            sourceDef.EquipCategory is not { } category ||
            !EquipSlots.IsValid(category, equipSlot))
        {
            return InventoryOpResult.Fail(ResultCode.NotEquippable);
        }

        var equippedSlotByte = (byte)equipSlot;
        var existing = inventory.Find(ContainerId.Equipped, equippedSlotByte);

        if (existing is null)
        {
            source.Container = ContainerId.Equipped;
            source.Slot = equippedSlotByte;
            return InventoryOpResult.Success(new SlotRef(container, slot), new SlotRef(ContainerId.Equipped, equippedSlotByte));
        }

        // Intercambio: el que estaba puesto necesita un hueco libre en su propia bolsa.
        if (!catalog.TryGet(existing.DefKey, out var existingDef))
        {
            return InventoryOpResult.Fail(ResultCode.UnknownError);
        }

        var homeContainer = InventoryConstants.AllowedContainer(existingDef.Type);
        var freeSlot = inventory.FindEmptySlot(homeContainer);
        if (freeSlot is null)
        {
            return InventoryOpResult.Fail(ResultCode.InventoryFull);
        }

        existing.Container = homeContainer;
        existing.Slot = freeSlot.Value;
        source.Container = ContainerId.Equipped;
        source.Slot = equippedSlotByte;

        return InventoryOpResult.Success(
            new SlotRef(container, slot),
            new SlotRef(ContainerId.Equipped, equippedSlotByte),
            new SlotRef(homeContainer, freeSlot.Value));
    }

    /// <summary>Desequipar, de vuelta a la bolsa que le toque por su tipo. Falla si esa bolsa está llena.</summary>
    public static InventoryOpResult TryUnequip(PlayerInventory inventory, ItemCatalog catalog, EquipSlot equipSlot)
    {
        var equippedSlotByte = (byte)equipSlot;
        var existing = inventory.Find(ContainerId.Equipped, equippedSlotByte);
        if (existing is null)
        {
            return InventoryOpResult.Fail(ResultCode.ItemNotFound);
        }

        if (!catalog.TryGet(existing.DefKey, out var def))
        {
            return InventoryOpResult.Fail(ResultCode.UnknownError);
        }

        var homeContainer = InventoryConstants.AllowedContainer(def.Type);
        var freeSlot = inventory.FindEmptySlot(homeContainer);
        if (freeSlot is null)
        {
            return InventoryOpResult.Fail(ResultCode.InventoryFull);
        }

        existing.Container = homeContainer;
        existing.Slot = freeSlot.Value;

        return InventoryOpResult.Success(new SlotRef(ContainerId.Equipped, equippedSlotByte), new SlotRef(homeContainer, freeSlot.Value));
    }

    /// <summary>
    /// Stats derivados de base + lo que dé el equipo puesto (FASE-06 §2 D5), incluidos ataque y
    /// defensa desde la Fase 9.
    /// <para>
    /// Ataque y defensa son <b>provisionales</b>, igual que el resto de números de combate: la
    /// Fase 10 los reajusta con la curva real. Salen de fuerza y vitalidad efectivas, así que el
    /// equipo ya cuenta a través de sus bonos y no hace falta un campo nuevo en los ítems.
    /// </para>
    /// </summary>
    public static DerivedStats ComputeDerivedStats(
        PlayerInventory inventory, ItemCatalog catalog, ClassDefinition classDef,
        int baseStr, int baseInt, int baseVit, int baseDex, int level)
    {
        // HpPerLevel/MpPerLevel por encima del nivel 1 (Fase 10 §2 D3): hasta ahora la vida
        // máxima era fija toda la partida, algo que sólo daba igual porque nada subía de nivel.
        var hpMax = classDef.BaseHp + (classDef.HpPerLevel * (level - 1));
        var mpMax = classDef.BaseMp + (classDef.MpPerLevel * (level - 1));
        var str = baseStr;
        var intStat = baseInt;
        var vit = baseVit;
        var dex = baseDex;

        foreach (var stack in inventory.Stacks)
        {
            if (stack.Container != ContainerId.Equipped || !catalog.TryGet(stack.DefKey, out var def))
            {
                continue;
            }

            hpMax += def.BonusHp;
            mpMax += def.BonusMp;
            str += def.BonusStr;
            intStat += def.BonusInt;
            vit += def.BonusVit;
            dex += def.BonusDex;
        }

        return new DerivedStats(hpMax, mpMax, str, intStat, vit, dex, Attack: str, Defense: vit / 2);
    }
}
