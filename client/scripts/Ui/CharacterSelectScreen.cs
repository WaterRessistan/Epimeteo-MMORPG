using Epimeteo.Client.Net;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Godot;

namespace Epimeteo.Client.Ui;

/// <summary>
/// Pantalla de selección de personaje (Fase 3): 5 slots fijos, crear en uno vacío, borrar (con
/// confirmación) o entrar en uno existente. La "apariencia" es sólo un índice de paleta —
/// FASE-03-personajes.md §4, los assets siguen en placeholder.
/// </summary>
public partial class CharacterSelectScreen : Control
{
    private const int SlotCount = 5;
    private const int PaletteCount = 4;

    private static readonly (string Key, string Label)[] Classes =
    [
        ("class.warrior", "Guerrero"),
        ("class.mage", "Mago"),
        ("class.hybrid", "Híbrido"),
    ];

    private NetClient _net = null!;
    private Label _messageLabel = null!;
    private VBoxContainer _slots = null!;
    private VBoxContainer _createPanel = null!;
    private LineEdit _nameEdit = null!;
    private OptionButton _classOption = null!;
    private OptionButton _paletteOption = null!;
    private Button _confirmCreate = null!;
    private Button _cancelCreate = null!;
    private ConfirmationDialog _deleteConfirm = null!;

    private CharacterSummary?[] _characters = new CharacterSummary?[SlotCount];
    private int _createSlot = -1;
    private long _pendingDeleteId;

    public override void _Ready()
    {
        _net = GetNode<NetClient>("/root/NetClient");
        _messageLabel = GetNode<Label>("Layout/Message");
        _slots = GetNode<VBoxContainer>("Layout/Slots");
        _createPanel = GetNode<VBoxContainer>("Layout/CreatePanel");
        _nameEdit = GetNode<LineEdit>("Layout/CreatePanel/Name");
        _classOption = GetNode<OptionButton>("Layout/CreatePanel/ClassOption");
        _paletteOption = GetNode<OptionButton>("Layout/CreatePanel/PaletteOption");
        _confirmCreate = GetNode<Button>("Layout/CreatePanel/CreateButtons/Confirm");
        _cancelCreate = GetNode<Button>("Layout/CreatePanel/CreateButtons/Cancel");
        _deleteConfirm = GetNode<ConfirmationDialog>("DeleteConfirm");

        foreach (var (_, label) in Classes)
        {
            _classOption.AddItem(label);
        }

        for (var i = 0; i < PaletteCount; i++)
        {
            _paletteOption.AddItem($"Paleta {i + 1}");
        }

        _confirmCreate.Pressed += OnConfirmCreatePressed;
        _cancelCreate.Pressed += HideCreatePanel;
        _deleteConfirm.Confirmed += OnDeleteConfirmed;

        _net.CharListReceived += OnCharList;
        _net.CharCreateResultReceived += OnCharCreateResult;
        _net.CharDeleteResultReceived += OnCharDeleteResult;
        _net.WorldEnterReceived += OnWorldEnter;
        _net.Kicked += OnKicked;

        HideCreatePanel();
        _net.RequestCharList();
    }

    public override void _ExitTree()
    {
        _confirmCreate.Pressed -= OnConfirmCreatePressed;
        _cancelCreate.Pressed -= HideCreatePanel;
        _deleteConfirm.Confirmed -= OnDeleteConfirmed;

        _net.CharListReceived -= OnCharList;
        _net.CharCreateResultReceived -= OnCharCreateResult;
        _net.CharDeleteResultReceived -= OnCharDeleteResult;
        _net.WorldEnterReceived -= OnWorldEnter;
        _net.Kicked -= OnKicked;
    }

    private void OnCharList(S2CCharList list)
    {
        _characters = new CharacterSummary?[SlotCount];
        foreach (var summary in list.Characters)
        {
            if (summary.Slot is >= 0 and < SlotCount)
            {
                _characters[summary.Slot] = summary;
            }
        }

        RebuildSlots();
    }

    private void RebuildSlots()
    {
        foreach (var child in _slots.GetChildren())
        {
            child.QueueFree();
        }

        for (var slot = 0; slot < SlotCount; slot++)
        {
            _slots.AddChild(BuildSlotRow(slot, _characters[slot]));
        }
    }

    private Control BuildSlotRow(int slot, CharacterSummary? character)
    {
        var row = new HBoxContainer();

        var label = new Label
        {
            Text = character is null
                ? $"{slot + 1}. (vacío)"
                : $"{slot + 1}. {character.Name} — {ClassLabel(character.ClassKey)} nv.{character.Level}",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddChild(label);

        if (character is null)
        {
            var create = new Button { Text = "Crear" };
            create.Pressed += () => ShowCreatePanel(slot);
            row.AddChild(create);
        }
        else
        {
            var enter = new Button { Text = "Entrar" };
            enter.Pressed += () => _net.SelectCharacter(character);
            row.AddChild(enter);

            var delete = new Button { Text = "Borrar" };
            delete.Pressed += () => ConfirmDelete(character.Id);
            row.AddChild(delete);
        }

        return row;
    }

    private static string ClassLabel(string classKey)
    {
        foreach (var (key, label) in Classes)
        {
            if (key == classKey)
            {
                return label;
            }
        }

        return classKey;
    }

    private void ShowCreatePanel(int slot)
    {
        _createSlot = slot;
        _nameEdit.Text = string.Empty;
        _messageLabel.Text = string.Empty;
        _createPanel.Visible = true;
    }

    private void HideCreatePanel()
    {
        _createSlot = -1;
        _createPanel.Visible = false;
    }

    private void OnConfirmCreatePressed()
    {
        if (_createSlot < 0)
        {
            return;
        }

        var classKey = Classes[_classOption.Selected].Key;
        _net.CreateCharacter(_nameEdit.Text, classKey, _createSlot, (byte)_paletteOption.Selected);
    }

    private void OnCharCreateResult(S2CCharCreateResult result)
    {
        if (result.Ok)
        {
            HideCreatePanel();
            _messageLabel.Modulate = Colors.White;
            _messageLabel.Text = string.Empty;
            _net.RequestCharList();
            return;
        }

        _messageLabel.Modulate = Colors.Salmon;
        _messageLabel.Text = ResultCodeText.Describe(result.Code);
    }

    private void ConfirmDelete(long characterId)
    {
        _pendingDeleteId = characterId;
        _deleteConfirm.PopupCentered();
    }

    private void OnDeleteConfirmed() => _net.DeleteCharacter(_pendingDeleteId, confirm: true);

    private void OnCharDeleteResult(S2CCharDeleteResult result)
    {
        if (result.Ok)
        {
            _net.RequestCharList();
            return;
        }

        _messageLabel.Modulate = Colors.Salmon;
        _messageLabel.Text = ResultCodeText.Describe(result.Code);
    }

    private void OnWorldEnter(S2CWorldEnter enter) => GetTree().ChangeSceneToFile("res://scenes/World.tscn");

    private void OnKicked(KickReason reason, ResultCode detail, int serverProtocolVersion)
    {
        GD.Print(ResultCodeText.Describe(reason, detail, serverProtocolVersion));
        GetTree().ChangeSceneToFile("res://scenes/Connect.tscn");
    }
}
