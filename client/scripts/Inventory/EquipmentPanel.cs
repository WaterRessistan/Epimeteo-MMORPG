using System;
using System.Collections.Generic;
using Epimeteo.Shared.Data;
using Godot;

namespace Epimeteo.Client.Inventory;

/// <summary>
/// Los 12 huecos de equipo (paperdoll de texto: sin arte todavía, CLAUDE.md §5) más los stats
/// derivados que llegan en cada <c>EquipmentUpdate</c> (FASE-06 §2 D5).
/// </summary>
public partial class EquipmentPanel : PanelContainer
{
    private static readonly EquipSlot[] SlotOrder =
    [
        EquipSlot.Head, EquipSlot.Amulet, EquipSlot.Cloak,
        EquipSlot.MainHand, EquipSlot.Chest, EquipSlot.OffHand,
        EquipSlot.Hands, EquipSlot.Legs, EquipSlot.Tool,
        EquipSlot.Ring1, EquipSlot.Ring2, EquipSlot.Feet,
    ];

    private readonly Dictionary<EquipSlot, ItemSlot> _slots = [];
    private Label _statsLabel = null!;

    /// <inheritdoc />
    public override void _Ready()
    {
        var layout = new VBoxContainer();
        AddChild(layout);

        layout.AddChild(new Label { Text = "Equipo" });

        var grid = new GridContainer { Columns = 3 };
        layout.AddChild(grid);

        foreach (var equipSlot in SlotOrder)
        {
            var cell = new VBoxContainer();
            cell.AddChild(new Label { Text = NameOf(equipSlot), HorizontalAlignment = HorizontalAlignment.Center });

            var itemSlot = ItemSlot.ForEquip(equipSlot);
            cell.AddChild(itemSlot);
            _slots[equipSlot] = itemSlot;

            grid.AddChild(cell);
        }

        _statsLabel = new Label();
        layout.AddChild(_statsLabel);
    }

    /// <summary>Conecta el soltado de todos los huecos a un manejador común (lo decide <c>InventoryScreen</c>).</summary>
    public void Configure(Action<ItemSlot, ItemSlot> onDrop)
    {
        foreach (var (_, slot) in _slots)
        {
            slot.OnItemDropped = source => onDrop(source, slot);
        }
    }

    public void Refresh(InventoryState state)
    {
        foreach (var (equipSlot, itemSlot) in _slots)
        {
            itemSlot.Item = state.EquippedAt(equipSlot);
        }

        _statsLabel.Text =
            $"HP {state.HpMax}  MP {state.MpMax}  STR {state.StrEffective}  " +
            $"INT {state.IntEffective}  VIT {state.VitEffective}  DEX {state.DexEffective}";
    }

    private static string NameOf(EquipSlot slot) => slot switch
    {
        EquipSlot.MainHand => "Mano ppal.",
        EquipSlot.OffHand => "Secundaria",
        EquipSlot.Head => "Cabeza",
        EquipSlot.Chest => "Pecho",
        EquipSlot.Hands => "Manos",
        EquipSlot.Legs => "Piernas",
        EquipSlot.Feet => "Pies",
        EquipSlot.Cloak => "Capa",
        EquipSlot.Ring1 => "Anillo 1",
        EquipSlot.Ring2 => "Anillo 2",
        EquipSlot.Amulet => "Amuleto",
        EquipSlot.Tool => "Herramienta",
        _ => slot.ToString(),
    };
}
