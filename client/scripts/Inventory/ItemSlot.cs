using System;
using System.Collections.Generic;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net.Messages;
using Godot;

namespace Epimeteo.Client.Inventory;

/// <summary>
/// Un hueco de inventario dibujado: o una posición de bolsa (<see cref="Container"/> +
/// <see cref="Slot"/>) o un hueco de equipo (<see cref="EquipSlotValue"/>), nunca los dos. Sin
/// arte (CLAUDE.md §5): rectángulo, texto del nombre y cantidad, igual que <c>WorldRenderer</c>
/// dibuja entidades sin sprites.
/// <para>
/// El arrastrar y soltar usa la API nativa de Godot para <c>Control</c>
/// (<see cref="_GetDragData"/>/<see cref="_CanDropData"/>/<see cref="_DropData"/>): no hay
/// mecanismo propio que mantener. El cliente no valida reglas de negocio al soltar —el servidor
/// ya lo hace y manda <c>SystemMessage</c> si no vale (FASE-06 §5)— sólo decide **qué mensaje**
/// mandar según de qué tipo son el origen y el destino.
/// </para>
/// </summary>
public partial class ItemSlot : Control
{
    private const int SlotSize = 40;

    private static readonly Color BorderColor = new(0.55f, 0.55f, 0.6f);
    private static readonly Color BackgroundColor = new(0.16f, 0.16f, 0.2f);
    private static readonly Color HoverBackgroundColor = new(0.24f, 0.24f, 0.3f);
    private static readonly Color TextColor = new(0.9f, 0.9f, 0.92f);
    private static readonly Color QuantityColor = new(1f, 0.85f, 0.4f);

    private Font? _font;
    private bool _hovering;

    /// <summary>Contenedor de bolsa, o <c>null</c> si este slot es de equipo.</summary>
    public ContainerId? Container { get; private set; }

    /// <summary>Posición dentro de la bolsa. Sólo válido si <see cref="Container"/> no es <c>null</c>.</summary>
    public byte Slot { get; private set; }

    /// <summary>Hueco de equipo, o <c>null</c> si este slot es de bolsa.</summary>
    public EquipSlot? EquipSlotValue { get; private set; }

    private ItemStackInfo? _item;

    /// <summary>Lo que hay aquí ahora mismo, o <c>null</c> si está vacío.</summary>
    public ItemStackInfo? Item
    {
        get => _item;
        set
        {
            _item = value;
            TooltipText = value is null ? string.Empty : DescribeTooltip(value);
            QueueRedraw();
        }
    }

    /// <summary>Se llama al soltar algo aquí, con el descriptor de origen. El escenario decide qué mensaje mandar.</summary>
    public Action<ItemSlot>? OnItemDropped { get; set; }

    /// <summary>Clic izquierdo con algo dentro: la pantalla decide si tiene sentido "usarlo" (sólo consumibles).</summary>
    public Action<ItemSlot>? OnClicked { get; set; }

    public static ItemSlot ForBag(ContainerId container, byte slot)
    {
        var itemSlot = new ItemSlot { Container = container, Slot = slot };
        return itemSlot;
    }

    public static ItemSlot ForEquip(EquipSlot equipSlot)
    {
        var itemSlot = new ItemSlot { EquipSlotValue = equipSlot };
        return itemSlot;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        CustomMinimumSize = new Vector2(SlotSize, SlotSize);
        MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () => { _hovering = true; QueueRedraw(); };
        MouseExited += () => { _hovering = false; QueueRedraw(); };
    }

    /// <inheritdoc />
    public override void _GuiInput(InputEvent @event)
    {
        if (Item is not null && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            OnClicked?.Invoke(this);
        }
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, _hovering ? HoverBackgroundColor : BackgroundColor);
        DrawRect(rect, BorderColor, filled: false, width: 1f);

        if (Item is not { } item || _font is null)
        {
            return;
        }

        // Sin catálogo de ítems en el cliente todavía en esta vista: se recorta la clave del
        // ítem ("item.iron_sword" → "iron_sword") en vez de pedirle el nombre a un catálogo que
        // esta pantalla no carga. Suficiente para validar la tubería (FASE-06 §7).
        var shortName = item.DefKey.Contains('.') ? item.DefKey[(item.DefKey.IndexOf('.') + 1)..] : item.DefKey;
        DrawString(_font, new Vector2(3, Size.Y - 20), shortName, HorizontalAlignment.Left, Size.X - 6, 8, TextColor);

        if (item.Quantity > 1)
        {
            DrawString(_font, new Vector2(3, 12), item.Quantity.ToString(), HorizontalAlignment.Left, -1, 10, QuantityColor);
        }
    }

    /// <inheritdoc />
    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (Item is null)
        {
            return default;
        }

        var preview = new Label { Text = Item.DefKey, Modulate = new Color(1, 1, 1, 0.8f) };
        SetDragPreview(preview);

        return new Godot.Collections.Dictionary { ["sourceSlot"] = this };
    }

    /// <inheritdoc />
    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        // La validación de negocio la hace el servidor; aquí sólo se comprueba la forma del
        // payload para no soltar cualquier cosa arrastrada desde fuera del inventario.
        return data.VariantType == Variant.Type.Dictionary &&
               data.AsGodotDictionary().TryGetValue("sourceSlot", out var value) &&
               value.Obj is ItemSlot;
    }

    /// <inheritdoc />
    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (data.AsGodotDictionary()["sourceSlot"].Obj is ItemSlot source && source != this)
        {
            OnItemDropped?.Invoke(source);
        }
    }

    private static string DescribeTooltip(ItemStackInfo item)
    {
        var lines = new List<string> { item.DefKey, $"Cantidad: {item.Quantity}" };
        if (item.Quality > 0)
        {
            lines.Add($"Calidad: {item.Quality}");
        }

        return string.Join('\n', lines);
    }
}
