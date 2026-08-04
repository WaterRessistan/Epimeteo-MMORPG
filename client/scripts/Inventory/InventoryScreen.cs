using System.Collections.Generic;
using Epimeteo.Client.Net;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net.Messages;
using Godot;

namespace Epimeteo.Client.Inventory;

/// <summary>
/// Overlay de inventario (tecla <c>I</c>, <c>toggle_inventory</c>): pestañas de bolsas +
/// equipo + tooltips. Sin arte (CLAUDE.md §5): la tubería de red es lo que importa aquí, no la
/// estética (FASE-06 §7).
/// <para>
/// Autocontenido a propósito, igual que las demás pantallas de <c>Ui/</c>: lee <c>NetClient</c>
/// del autoload él solo, así que <c>World.tscn</c> sólo tiene que tenerlo como hijo, sin que
/// <c>WorldScreen</c> necesite saber que existe.
/// </para>
/// </summary>
public partial class InventoryScreen : CanvasLayer
{
    private readonly InventoryState _state = new();
    private readonly Dictionary<ContainerId, Dictionary<byte, ItemSlot>> _bagSlots = [];

    private NetClient _net = null!;
    private EquipmentPanel _equipment = null!;
    private Label _message = null!;
    private Control _root = null!;
    private double _messageTimer;

    /// <inheritdoc />
    public override void _Ready()
    {
        _net = GetNode<NetClient>("/root/NetClient");

        _root = new PanelContainer
        {
            Visible = false,
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -260, OffsetRight = 260, OffsetTop = -180, OffsetBottom = 180,
        };
        AddChild(_root);

        var layout = new VBoxContainer();
        _root.AddChild(layout);
        layout.AddChild(new Label { Text = "Inventario (I para cerrar)" });

        var tabs = new TabContainer();
        layout.AddChild(tabs);
        BuildBagTab(tabs, "General", ContainerId.General, InventoryConstants.GeneralCapacity);
        BuildBagTab(tabs, "Armas", ContainerId.WeaponBag, InventoryConstants.WeaponBagCapacity);
        BuildBagTab(tabs, "Armaduras", ContainerId.ArmorBag, InventoryConstants.ArmorBagCapacity);

        _equipment = new EquipmentPanel();
        layout.AddChild(_equipment);
        _equipment.Configure(HandleDrop);

        _message = new Label { Modulate = new Color(1f, 0.7f, 0.4f) };
        layout.AddChild(_message);

        _net.InventoryFullReceived += OnInventoryFull;
        _net.InventoryDeltaReceived += OnInventoryDelta;
        _net.EquipmentUpdateReceived += OnEquipmentUpdate;
        _net.SystemMessageReceived += OnSystemMessage;
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _net.InventoryFullReceived -= OnInventoryFull;
        _net.InventoryDeltaReceived -= OnInventoryDelta;
        _net.EquipmentUpdateReceived -= OnEquipmentUpdate;
        _net.SystemMessageReceived -= OnSystemMessage;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed("toggle_inventory"))
        {
            _root.Visible = !_root.Visible;
        }

        if (_messageTimer > 0)
        {
            _messageTimer -= delta;
            if (_messageTimer <= 0)
            {
                _message.Text = string.Empty;
            }
        }
    }

    private void BuildBagTab(TabContainer tabs, string title, ContainerId container, int capacity)
    {
        var grid = new GridContainer { Columns = 6, Name = title };
        tabs.AddChild(grid);

        var slots = new Dictionary<byte, ItemSlot>();
        _bagSlots[container] = slots;

        for (byte slot = 0; slot < capacity; slot++)
        {
            var itemSlot = ItemSlot.ForBag(container, slot);
            itemSlot.OnItemDropped = source => HandleDrop(source, itemSlot);
            itemSlot.OnClicked = HandleClick;
            slots[slot] = itemSlot;
            grid.AddChild(itemSlot);
        }
    }

    /// <summary>
    /// Traduce un arrastre a la intención que le toca (FASE-06 §7): el cliente no decide si vale,
    /// sólo elige el mensaje — el servidor manda <c>SystemMessage</c> si no lo acepta.
    /// </summary>
    private void HandleDrop(ItemSlot source, ItemSlot target)
    {
        if (source.EquipSlotValue is { } fromEquip)
        {
            // Se suelte donde se suelte, sacar algo del equipo es siempre "desequipar": el
            // servidor decide sólo a dónde vuelve (FASE-06 §2 D4).
            _net.SendUnequip(fromEquip);
            return;
        }

        if (source.Container is not { } fromContainer)
        {
            return;
        }

        if (target.EquipSlotValue is { } toEquip)
        {
            _net.SendEquip(fromContainer, source.Slot, toEquip);
            return;
        }

        if (target.Container is { } toContainer)
        {
            var quantity = source.Item?.Quantity ?? 1;
            _net.SendInvMove(fromContainer, source.Slot, toContainer, target.Slot, quantity);
        }
    }

    private void HandleClick(ItemSlot slot)
    {
        if (slot.Container is { } container)
        {
            _net.SendInvUse(container, slot.Slot);
        }
    }

    private void OnInventoryFull(S2CInventoryFull full)
    {
        _state.ApplyFull(full);
        RefreshBags();
    }

    private void OnInventoryDelta(S2CInventoryDelta delta)
    {
        _state.ApplyDelta(delta);
        RefreshBags();
    }

    private void OnEquipmentUpdate(S2CEquipmentUpdate update)
    {
        _state.ApplyEquipment(update);
        _equipment.Refresh(_state);
    }

    private void OnSystemMessage(S2CSystemMessage message)
    {
        _message.Text = message.Key;
        _messageTimer = 4.0;
    }

    private void RefreshBags()
    {
        foreach (var (container, slots) in _bagSlots)
        {
            foreach (var (slot, itemSlot) in slots)
            {
                itemSlot.Item = _state.At(container, slot);
            }
        }
    }
}
