using Epimeteo.Server.Content;
using Epimeteo.Server.Inventory;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>
/// Sin Postgres ni tick: <see cref="InventorySystem"/> es puro sobre <see cref="PlayerInventory"/>,
/// igual que <c>MovementSystemTests</c> lo es sobre <c>MoveState</c>. Usa el catálogo real de
/// <c>content/items/</c> (mismo criterio que <c>MapCatalogTests</c>): si alguien cambia un ítem de
/// producción de forma que rompe estas reglas, salta aquí.
/// </summary>
public sealed class InventorySystemTests
{
    private static readonly ItemCatalog Items = new(ContentPaths.ResolveContentRoot());
    private static readonly ClassCatalog Classes = new(ContentPaths.ResolveContentRoot());

    private static ItemStack Stack(string defKey, ContainerId container, byte slot, int quantity = 1) => new()
    {
        DefKey = defKey,
        Container = container,
        Slot = slot,
        Quantity = quantity,
    };

    private static PlayerInventory Inventory(params ItemStack[] stacks) => new(stacks);

    // ── InvMove ──────────────────────────────────────────────────────────

    [Fact]
    public void Move_AHuecoVacio_Reubica()
    {
        var sword = Stack("item.iron_sword", ContainerId.WeaponBag, 0);
        var inv = Inventory(sword);

        var result = InventorySystem.TryMove(inv, Items, ContainerId.WeaponBag, 0, ContainerId.WeaponBag, 5, 1);

        Assert.True(result.Ok);
        Assert.Equal(ContainerId.WeaponBag, sword.Container);
        Assert.Equal(5, sword.Slot);
        Assert.Equal(2, result.Touched.Count);
    }

    [Fact]
    public void Move_AlMismoItem_ApilaHastaElMaximoYDejaElResto()
    {
        // health_potion apila a 20.
        var origin = Stack("item.health_potion", ContainerId.General, 0, quantity: 15);
        var target = Stack("item.health_potion", ContainerId.General, 1, quantity: 12);
        var inv = Inventory(origin, target);

        var result = InventorySystem.TryMove(inv, Items, ContainerId.General, 0, ContainerId.General, 1, 15);

        Assert.True(result.Ok);
        Assert.Equal(20, target.Quantity);
        Assert.Equal(7, origin.Quantity); // 15 + 15 = 30, sólo caben 8 más (12->20); sobran 7 en origen
    }

    [Fact]
    public void Move_ApilarYaAlMaximo_NoCambiaNada()
    {
        var origin = Stack("item.iron_ore", ContainerId.General, 0, quantity: 5);
        var target = Stack("item.iron_ore", ContainerId.General, 1, quantity: 99);
        var inv = Inventory(origin, target);

        var result = InventorySystem.TryMove(inv, Items, ContainerId.General, 0, ContainerId.General, 1, 5);

        Assert.True(result.Ok);
        Assert.Empty(result.Touched);
        Assert.Equal(5, origin.Quantity);
        Assert.Equal(99, target.Quantity);
    }

    [Fact]
    public void Move_UnaEspadaALaBolsaDeArmaduras_Falla()
    {
        var sword = Stack("item.iron_sword", ContainerId.WeaponBag, 0);
        var inv = Inventory(sword);

        var result = InventorySystem.TryMove(inv, Items, ContainerId.WeaponBag, 0, ContainerId.ArmorBag, 0, 1);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.NotEquippable, result.Code);
        Assert.Equal(ContainerId.WeaponBag, sword.Container); // nada cambió
    }

    [Fact]
    public void Move_ItemsDistintosAHuecoOcupado_Intercambia()
    {
        var sword = Stack("item.iron_sword", ContainerId.WeaponBag, 0);
        var shield = Stack("item.wooden_shield", ContainerId.WeaponBag, 1);
        var inv = Inventory(sword, shield);

        var result = InventorySystem.TryMove(inv, Items, ContainerId.WeaponBag, 0, ContainerId.WeaponBag, 1, 1);

        Assert.True(result.Ok);
        Assert.Equal(1, sword.Slot);
        Assert.Equal(0, shield.Slot);
    }

    [Fact]
    public void Move_DividirPilaConCantidadParcial_CreaUnSegundoStack()
    {
        var ore = Stack("item.iron_ore", ContainerId.General, 0, quantity: 10);
        var inv = Inventory(ore);

        var result = InventorySystem.TryMove(inv, Items, ContainerId.General, 0, ContainerId.General, 1, 3);

        Assert.True(result.Ok);
        Assert.Equal(7, ore.Quantity);
        Assert.Equal(2, inv.Stacks.Count);

        var split = inv.Find(ContainerId.General, 1);
        Assert.NotNull(split);
        Assert.Equal("item.iron_ore", split!.DefKey);
        Assert.Equal(3, split.Quantity);
    }

    [Fact]
    public void Move_DesdeHuecoVacio_ItemNotFound()
    {
        var inv = Inventory();

        var result = InventorySystem.TryMove(inv, Items, ContainerId.General, 0, ContainerId.General, 1, 1);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.ItemNotFound, result.Code);
    }

    [Fact]
    public void Move_TocandoElContenedorEquipado_Falla()
    {
        var inv = Inventory(Stack("item.iron_sword", ContainerId.Equipped, (byte)EquipSlot.MainHand));

        var result = InventorySystem.TryMove(
            inv, Items, ContainerId.Equipped, (byte)EquipSlot.MainHand, ContainerId.WeaponBag, 0, 1);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.NotEquippable, result.Code);
    }

    // ── InvDrop ──────────────────────────────────────────────────────────

    [Fact]
    public void Drop_MasDeLoQueHay_NotEnoughItems()
    {
        var ore = Stack("item.iron_ore", ContainerId.General, 0, quantity: 3);
        var inv = Inventory(ore);

        var result = InventorySystem.TryDrop(inv, ContainerId.General, 0, 5);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.NotEnoughItems, result.Code);
        Assert.Equal(3, ore.Quantity);
    }

    [Fact]
    public void Drop_TodoElStack_LoElimina()
    {
        var ore = Stack("item.iron_ore", ContainerId.General, 0, quantity: 3);
        var inv = Inventory(ore);

        var result = InventorySystem.TryDrop(inv, ContainerId.General, 0, 3);

        Assert.True(result.Ok);
        Assert.Null(inv.Find(ContainerId.General, 0));
    }

    // ── InvUse ───────────────────────────────────────────────────────────

    [Fact]
    public void Use_PocionDeCuracion_ReduceLaCantidadYDevuelveLaCuracion()
    {
        var potions = Stack("item.health_potion", ContainerId.General, 0, quantity: 3);
        var inv = Inventory(potions);

        var result = InventorySystem.TryUse(inv, Items, ContainerId.General, 0);

        Assert.True(result.Ok);
        Assert.Equal(30, result.HealAmount);
        Assert.Equal(2, potions.Quantity);
    }

    [Fact]
    public void Use_LaUltimaPocion_BorraElStack()
    {
        var potions = Stack("item.health_potion", ContainerId.General, 0, quantity: 1);
        var inv = Inventory(potions);

        var result = InventorySystem.TryUse(inv, Items, ContainerId.General, 0);

        Assert.True(result.Ok);
        Assert.Null(inv.Find(ContainerId.General, 0));
    }

    [Fact]
    public void Use_UnMaterial_NoEsUsable()
    {
        var ore = Stack("item.iron_ore", ContainerId.General, 0, quantity: 1);
        var inv = Inventory(ore);

        var result = InventorySystem.TryUse(inv, Items, ContainerId.General, 0);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.NotEquippable, result.Code);
        Assert.Equal(1, ore.Quantity); // nada cambió
    }

    // ── Equip / Unequip ──────────────────────────────────────────────────

    [Fact]
    public void Equip_UnArmaEnMainHand_TerminaEnEquipadoSlotCero()
    {
        var sword = Stack("item.iron_sword", ContainerId.WeaponBag, 0);
        var inv = Inventory(sword);

        var result = InventorySystem.TryEquip(inv, Items, ContainerId.WeaponBag, 0, EquipSlot.MainHand);

        Assert.True(result.Ok);
        Assert.Equal(ContainerId.Equipped, sword.Container);
        Assert.Equal((byte)EquipSlot.MainHand, sword.Slot);
    }

    [Fact]
    public void Equip_UnArmaEnHead_NotEquippable()
    {
        var sword = Stack("item.iron_sword", ContainerId.WeaponBag, 0);
        var inv = Inventory(sword);

        var result = InventorySystem.TryEquip(inv, Items, ContainerId.WeaponBag, 0, EquipSlot.Head);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.NotEquippable, result.Code);
        Assert.Equal(ContainerId.WeaponBag, sword.Container); // nada cambió
    }

    [Fact]
    public void Equip_SobreHuecoOcupado_IntercambiaYElViejoVuelveASuBolsa()
    {
        // Dos espadas: una puesta, otra en la bolsa. Volver a equipar cambia cuál está puesta.
        var equipped = Stack("item.iron_sword", ContainerId.Equipped, (byte)EquipSlot.MainHand);
        var incoming = Stack("item.iron_sword", ContainerId.WeaponBag, 0);
        var inv = Inventory(equipped, incoming);

        var result = InventorySystem.TryEquip(inv, Items, ContainerId.WeaponBag, 0, EquipSlot.MainHand);

        Assert.True(result.Ok);
        Assert.Equal(ContainerId.Equipped, incoming.Container);
        Assert.Equal((byte)EquipSlot.MainHand, incoming.Slot);
        Assert.Equal(ContainerId.WeaponBag, equipped.Container);
        // El slot 0 seguía ocupado por "incoming" en el momento de buscar hueco libre: el
        // primero de verdad disponible es el 1.
        Assert.Equal(1, equipped.Slot);
    }

    [Fact]
    public void Equip_IntercambioSinSitioEnLaBolsa_InventoryFullYNadaCambia()
    {
        var equipped = Stack("item.iron_sword", ContainerId.Equipped, (byte)EquipSlot.MainHand);
        var incoming = Stack("item.iron_sword", ContainerId.WeaponBag, 0);

        // Llenar toda la bolsa de armas para que no quepa la espada saliente.
        var stacks = new List<ItemStack> { equipped, incoming };
        for (byte slot = 1; slot < InventoryConstants.WeaponBagCapacity; slot++)
        {
            stacks.Add(Stack("item.iron_sword", ContainerId.WeaponBag, slot));
        }

        var inv = Inventory([.. stacks]);

        var result = InventorySystem.TryEquip(inv, Items, ContainerId.WeaponBag, 0, EquipSlot.MainHand);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.InventoryFull, result.Code);
        Assert.Equal(ContainerId.Equipped, equipped.Container); // nada se movió
        Assert.Equal(ContainerId.WeaponBag, incoming.Container);
    }

    [Theory]
    [InlineData(EquipSlot.Ring1)]
    [InlineData(EquipSlot.Ring2)]
    public void Equip_UnAnillo_ValeEnCualquieraDeLosDosHuecos(EquipSlot ringSlot)
    {
        var ring = Stack("item.copper_ring", ContainerId.ArmorBag, 0);
        var inv = Inventory(ring);

        var result = InventorySystem.TryEquip(inv, Items, ContainerId.ArmorBag, 0, ringSlot);

        Assert.True(result.Ok);
        Assert.Equal((byte)ringSlot, ring.Slot);
    }

    [Fact]
    public void Equip_UnAnilloEnHead_NotEquippable()
    {
        var ring = Stack("item.copper_ring", ContainerId.ArmorBag, 0);
        var inv = Inventory(ring);

        var result = InventorySystem.TryEquip(inv, Items, ContainerId.ArmorBag, 0, EquipSlot.Head);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.NotEquippable, result.Code);
    }

    [Fact]
    public void Unequip_DevuelveElItemASuBolsa()
    {
        var sword = Stack("item.iron_sword", ContainerId.Equipped, (byte)EquipSlot.MainHand);
        var inv = Inventory(sword);

        var result = InventorySystem.TryUnequip(inv, Items, EquipSlot.MainHand);

        Assert.True(result.Ok);
        Assert.Equal(ContainerId.WeaponBag, sword.Container);
        Assert.Equal(0, sword.Slot);
    }

    [Fact]
    public void Unequip_HuecoVacio_ItemNotFound()
    {
        var inv = Inventory();

        var result = InventorySystem.TryUnequip(inv, Items, EquipSlot.MainHand);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.ItemNotFound, result.Code);
    }

    [Fact]
    public void Unequip_SinSitioEnLaBolsa_InventoryFullYNadaCambia()
    {
        var sword = Stack("item.iron_sword", ContainerId.Equipped, (byte)EquipSlot.MainHand);
        var stacks = new List<ItemStack> { sword };
        for (byte slot = 0; slot < InventoryConstants.WeaponBagCapacity; slot++)
        {
            stacks.Add(Stack("item.wooden_shield", ContainerId.WeaponBag, slot));
        }

        var inv = Inventory([.. stacks]);

        var result = InventorySystem.TryUnequip(inv, Items, EquipSlot.MainHand);

        Assert.False(result.Ok);
        Assert.Equal(ResultCode.InventoryFull, result.Code);
        Assert.Equal(ContainerId.Equipped, sword.Container);
    }

    // ── Stats derivados ──────────────────────────────────────────────────

    [Fact]
    public void ComputeDerivedStats_SinEquipo_EsSoloBaseMasClase()
    {
        Assert.True(Classes.TryGet("class.warrior", out var warrior));
        var inv = Inventory();

        var stats = InventorySystem.ComputeDerivedStats(inv, Items, warrior!, baseStr: 10, baseInt: 3, baseVit: 8, baseDex: 5, level: 1);

        Assert.Equal(warrior!.BaseHp, stats.HpMax);
        Assert.Equal(warrior.BaseMp, stats.MpMax);
        Assert.Equal(10, stats.StrEffective);
    }

    [Fact]
    public void ComputeDerivedStats_ConEquipo_SumaLosBonos()
    {
        Assert.True(Classes.TryGet("class.warrior", out var warrior));
        // leather_chest: +2 vit, +15 hp. copper_ring: +1 int, +5 mp.
        var inv = Inventory(
            Stack("item.leather_chest", ContainerId.Equipped, (byte)EquipSlot.Chest),
            Stack("item.copper_ring", ContainerId.Equipped, (byte)EquipSlot.Ring1));

        var stats = InventorySystem.ComputeDerivedStats(inv, Items, warrior!, baseStr: 10, baseInt: 3, baseVit: 8, baseDex: 5, level: 1);

        Assert.Equal(warrior!.BaseHp + 15, stats.HpMax);
        Assert.Equal(warrior.BaseMp + 5, stats.MpMax);
        Assert.Equal(8 + 2, stats.VitEffective);
        Assert.Equal(3 + 1, stats.IntEffective);
    }

    [Fact]
    public void ComputeDerivedStats_IgnoraLoQueNoEstaEquipado()
    {
        Assert.True(Classes.TryGet("class.warrior", out var warrior));
        var inv = Inventory(Stack("item.leather_chest", ContainerId.ArmorBag, 0)); // en la bolsa, no puesto

        var stats = InventorySystem.ComputeDerivedStats(inv, Items, warrior!, baseStr: 10, baseInt: 3, baseVit: 8, baseDex: 5, level: 1);

        Assert.Equal(warrior!.BaseHp, stats.HpMax);
    }
}
