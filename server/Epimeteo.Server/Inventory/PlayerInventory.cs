using Epimeteo.Shared.Data;

namespace Epimeteo.Server.Inventory;

/// <summary>
/// Todos los stacks de un jugador, en cualquier contenedor (0–3: general, armas, armaduras,
/// equipado). Direccionado por <c>(Container, Slot)</c>, igual que la fila de Postgres — no hay
/// traducción entre "cómo se guarda" y "cómo se referencia en memoria".
/// </summary>
public sealed class PlayerInventory
{
    private readonly List<ItemStack> _stacks;

    public PlayerInventory(IEnumerable<ItemStack> initial) => _stacks = [.. initial];

    /// <summary>Todos los stacks, en cualquier contenedor. Sólo lectura desde fuera.</summary>
    public IReadOnlyList<ItemStack> Stacks => _stacks;

    /// <summary><c>null</c> si el hueco está vacío.</summary>
    public ItemStack? Find(ContainerId container, byte slot) =>
        _stacks.Find(stack => stack.Container == container && stack.Slot == slot);

    /// <summary>El primer hueco vacío de una bolsa, o <c>null</c> si está llena.</summary>
    public byte? FindEmptySlot(ContainerId container)
    {
        var capacity = InventoryConstants.CapacityOf(container);
        for (byte slot = 0; slot < capacity; slot++)
        {
            if (Find(container, slot) is null)
            {
                return slot;
            }
        }

        return null;
    }

    internal void Add(ItemStack stack) => _stacks.Add(stack);

    internal void Remove(ItemStack stack) => _stacks.Remove(stack);
}
