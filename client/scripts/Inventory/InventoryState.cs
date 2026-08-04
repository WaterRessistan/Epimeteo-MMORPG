using System.Collections.Generic;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net.Messages;

namespace Epimeteo.Client.Inventory;

/// <summary>
/// Espejo cliente del <c>PlayerInventory</c> del servidor: recibe <c>InventoryFull</c>,
/// <c>InventoryDelta</c> y <c>EquipmentUpdate</c> y mantiene el estado para dibujar. Sin
/// predicción — a diferencia del movimiento, aquí no hay coste perceptible en esperar la
/// confirmación del servidor antes de mover un icono (FASE-06 §7).
/// </summary>
public sealed class InventoryState
{
    private readonly Dictionary<(ContainerId Container, byte Slot), ItemStackInfo> _bags = [];
    private readonly Dictionary<EquipSlot, ItemStackInfo> _equipped = [];

    /// <summary>Todo lo que hay en las bolsas (containers 0/1/2), por hueco.</summary>
    public IReadOnlyDictionary<(ContainerId Container, byte Slot), ItemStackInfo> Bags => _bags;

    /// <summary>Lo que hay puesto, por hueco de equipo.</summary>
    public IReadOnlyDictionary<EquipSlot, ItemStackInfo> Equipped => _equipped;

    public int HpMax { get; private set; }

    public int MpMax { get; private set; }

    public int StrEffective { get; private set; }

    public int IntEffective { get; private set; }

    public int VitEffective { get; private set; }

    public int DexEffective { get; private set; }

    public void ApplyFull(S2CInventoryFull full)
    {
        _bags.Clear();
        foreach (var item in full.Items)
        {
            _bags[(item.Container, item.Slot)] = item;
        }
    }

    public void ApplyDelta(S2CInventoryDelta delta)
    {
        foreach (var change in delta.Changes)
        {
            var key = (change.Container, change.Slot);
            if (change.Item is null)
            {
                _bags.Remove(key);
            }
            else
            {
                _bags[key] = change.Item;
            }
        }
    }

    public void ApplyEquipment(S2CEquipmentUpdate update)
    {
        _equipped.Clear();
        foreach (var item in update.Equipped)
        {
            _equipped[(EquipSlot)item.Slot] = item;
        }

        HpMax = update.HpMax;
        MpMax = update.MpMax;
        StrEffective = update.StrEffective;
        IntEffective = update.IntEffective;
        VitEffective = update.VitEffective;
        DexEffective = update.DexEffective;
    }

    /// <summary><c>null</c> si el hueco está vacío.</summary>
    public ItemStackInfo? At(ContainerId container, byte slot) => _bags.GetValueOrDefault((container, slot));

    /// <summary><c>null</c> si no hay nada puesto en ese hueco.</summary>
    public ItemStackInfo? EquippedAt(EquipSlot slot) => _equipped.GetValueOrDefault(slot);
}
