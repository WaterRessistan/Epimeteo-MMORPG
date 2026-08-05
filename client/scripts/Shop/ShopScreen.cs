using System.Collections.Generic;
using Epimeteo.Client.Inventory;
using Epimeteo.Client.Net;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net.Messages;
using Godot;

namespace Epimeteo.Client.Shop;

/// <summary>
/// Overlay de tienda (Fase 7): se abre solo al llegar un <c>ShopData</c> (lo pide
/// <c>WorldScreen</c> con la tecla <c>interact</c> al NPC más cercano) y se cierra con la misma
/// tecla o el botón. Sin arte (CLAUDE.md §5): igual que <c>InventoryScreen</c>, lo que importa
/// aquí es la tubería de red, no la estética.
/// <para>
/// Lleva su propio <see cref="InventoryState"/>, aparte del de <c>InventoryScreen</c>: cada
/// pantalla de <c>Ui/</c> es autocontenida y lee <c>NetClient</c> ella sola (mismo criterio que
/// FASE-06 §7), así que duplicar ese espejo barato es más simple que acoplar las dos pantallas.
/// </para>
/// </summary>
public partial class ShopScreen : CanvasLayer
{
    private readonly InventoryState _inventory = new();
    private readonly Dictionary<ContainerId, Dictionary<byte, ItemSlot>> _sellSlots = [];
    private readonly Dictionary<ContainerId, Dictionary<byte, ItemSlot>> _repairSlots = [];

    private NetClient _net = null!;
    private Control _root = null!;
    private Label _title = null!;
    private Label _gold = null!;
    private Label _message = null!;
    private VBoxContainer _buyList = null!;
    private TabContainer _tabs = null!;
    private Control _repairTab = null!;

    private S2CShopData? _shop;
    private double _messageTimer;
    private int _lastNpcEntityId;

    /// <summary>Si la tienda está abierta ahora mismo. <c>WorldScreen</c> lo usa para saber si <c>interact</c> abre o cierra.</summary>
    public bool IsOpen => _root.Visible;

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

        var header = new HBoxContainer();
        layout.AddChild(header);
        _title = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        header.AddChild(_title);
        _gold = new Label();
        header.AddChild(_gold);
        var closeButton = new Button { Text = "Cerrar (E)" };
        closeButton.Pressed += RequestClose;
        header.AddChild(closeButton);

        _tabs = new TabContainer();
        layout.AddChild(_tabs);

        _buyList = new VBoxContainer { Name = "Comprar" };
        _tabs.AddChild(_buyList);

        var sellTab = BuildBagGrid("Vender", _sellSlots, HandleSellClick);
        _tabs.AddChild(sellTab);

        _repairTab = BuildBagGrid("Reparar", _repairSlots, HandleRepairClick);
        _tabs.AddChild(_repairTab);

        _message = new Label { Modulate = new Color(1f, 0.7f, 0.4f) };
        layout.AddChild(_message);

        _net.ShopDataReceived += OnShopData;
        _net.ShopResultReceived += OnShopResult;
        _net.CurrencyUpdateReceived += OnCurrencyUpdate;
        _net.InventoryFullReceived += OnInventoryFull;
        _net.InventoryDeltaReceived += OnInventoryDelta;
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _net.ShopDataReceived -= OnShopData;
        _net.ShopResultReceived -= OnShopResult;
        _net.CurrencyUpdateReceived -= OnCurrencyUpdate;
        _net.InventoryFullReceived -= OnInventoryFull;
        _net.InventoryDeltaReceived -= OnInventoryDelta;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_messageTimer > 0)
        {
            _messageTimer -= delta;
            if (_messageTimer <= 0)
            {
                _message.Text = string.Empty;
            }
        }
    }

    /// <summary>Cierra la tienda y avisa al servidor. Lo llama <c>WorldScreen</c> al volver a pulsar <c>interact</c>.</summary>
    public void RequestClose()
    {
        if (!_root.Visible)
        {
            return;
        }

        _root.Visible = false;
        _shop = null;
        _net.SendShopClose();
    }

    private Control BuildBagGrid(
        string title, Dictionary<ContainerId, Dictionary<byte, ItemSlot>> slotsByContainer, System.Action<ItemSlot> onClicked)
    {
        var root = new VBoxContainer { Name = title };

        AddBagRow(root, slotsByContainer, ContainerId.General, InventoryConstants.GeneralCapacity, onClicked);
        AddBagRow(root, slotsByContainer, ContainerId.WeaponBag, InventoryConstants.WeaponBagCapacity, onClicked);
        AddBagRow(root, slotsByContainer, ContainerId.ArmorBag, InventoryConstants.ArmorBagCapacity, onClicked);

        return root;
    }

    private static void AddBagRow(
        VBoxContainer root, Dictionary<ContainerId, Dictionary<byte, ItemSlot>> slotsByContainer,
        ContainerId container, int capacity, System.Action<ItemSlot> onClicked)
    {
        var grid = new GridContainer { Columns = 6 };
        root.AddChild(grid);

        var slots = new Dictionary<byte, ItemSlot>();
        slotsByContainer[container] = slots;

        for (byte slot = 0; slot < capacity; slot++)
        {
            var itemSlot = ItemSlot.ForBag(container, slot);
            itemSlot.OnClicked = onClicked;
            slots[slot] = itemSlot;
            grid.AddChild(itemSlot);
        }
    }

    private void HandleSellClick(ItemSlot slot)
    {
        if (_shop is not { } shop || slot.Container is not { } container || slot.Item is not { } item)
        {
            return;
        }

        var shopSlot = FindShopSlot(shop, item.DefKey);
        if (shopSlot is null)
        {
            ShowMessage("Esta tienda no compra eso.");
            return;
        }

        _net.SendShopSell(container, slot.Slot, 1, shopSlot.PriceSell);
    }

    private void HandleRepairClick(ItemSlot slot)
    {
        if (slot.Container is not { } container || slot.Item is not { } item)
        {
            return;
        }

        if (item.DurabilityMax is null)
        {
            ShowMessage("Eso no se desgasta: no hay nada que reparar.");
            return;
        }

        if (item.Durability == item.DurabilityMax)
        {
            ShowMessage("Ya está al máximo de durabilidad.");
            return;
        }

        _net.SendShopRepair(container, slot.Slot);
    }

    private static ShopSlotInfo? FindShopSlot(S2CShopData shop, string defKey)
    {
        foreach (var slot in shop.Slots)
        {
            if (slot.DefKey == defKey)
            {
                return slot;
            }
        }

        return null;
    }

    private void OnShopData(S2CShopData data)
    {
        _shop = data;
        _root.Visible = true;
        _repairTab.Visible = data.CanRepair;

        _title.Text = data.DisplayName;
        _gold.Text = $"Oro: {_net.Gold}";

        RefreshBuyList(data);
        RefreshBagGrids();
    }

    private void OnShopResult(S2CShopResult result)
    {
        if (!result.Ok)
        {
            ShowMessage(result.Code.ToString());
        }
    }

    private void OnCurrencyUpdate(S2CCurrencyUpdate currency)
    {
        _gold.Text = $"Oro: {currency.Gold}";

        // Comprar/vender también cambia el stock del hueco que se tocó: como no hay un mensaje
        // dedicado a "esto es lo nuevo del stock" fuera de reabrir, se vuelve a pedir la tienda
        // entera — barato (un ShopOpen más) y así el panel de compra no se queda con precios o
        // existencias caducados tras la operación.
        if (_shop is not null)
        {
            _net.SendShopOpen(_lastNpcEntityId);
        }
    }

    /// <summary>
    /// <c>WorldScreen</c> se lo dice al mandar <c>ShopOpen</c>: hace falta para poder volver a
    /// pedir la tienda tras una compra/venta y refrescar el stock mostrado (ver <see cref="OnCurrencyUpdate"/>).
    /// </summary>
    public void NoteOpenedNpc(int npcEntityId) => _lastNpcEntityId = npcEntityId;

    private void OnInventoryFull(S2CInventoryFull full)
    {
        _inventory.ApplyFull(full);
        RefreshBagGrids();
    }

    private void OnInventoryDelta(S2CInventoryDelta delta)
    {
        _inventory.ApplyDelta(delta);
        RefreshBagGrids();
    }

    private void RefreshBuyList(S2CShopData data)
    {
        foreach (var child in _buyList.GetChildren())
        {
            child.QueueFree();
        }

        for (byte i = 0; i < data.Slots.Length; i++)
        {
            var slot = data.Slots[i];
            var shopSlot = i;

            var row = new HBoxContainer();
            var shortName = slot.DefKey.Contains('.') ? slot.DefKey[(slot.DefKey.IndexOf('.') + 1)..] : slot.DefKey;
            var stockText = slot.Stock < 0 ? "∞" : slot.Stock.ToString();

            row.AddChild(new Label
            {
                Text = $"{shortName} — {slot.PriceBuy}g (stock {stockText})",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            });

            var buyButton = new Button { Text = "Comprar", Disabled = slot.Stock == 0 };
            buyButton.Pressed += () => _net.SendShopBuy(shopSlot, 1, slot.PriceBuy);
            row.AddChild(buyButton);

            _buyList.AddChild(row);
        }
    }

    private void RefreshBagGrids()
    {
        RefreshBagGrid(_sellSlots);
        RefreshBagGrid(_repairSlots);
    }

    private void RefreshBagGrid(Dictionary<ContainerId, Dictionary<byte, ItemSlot>> slotsByContainer)
    {
        foreach (var (container, slots) in slotsByContainer)
        {
            foreach (var (slot, itemSlot) in slots)
            {
                itemSlot.Item = _inventory.At(container, slot);
            }
        }
    }

    private void ShowMessage(string text)
    {
        _message.Text = text;
        _messageTimer = 4.0;
    }
}
