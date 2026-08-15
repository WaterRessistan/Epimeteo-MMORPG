using Epimeteo.Client.Net;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net.Messages;
using Godot;

namespace Epimeteo.Client.Ui;

/// <summary>
/// Overlay de reparto de puntos de stat (tecla <c>K</c>, Fase 10): un botón "+1" por stat base y
/// los puntos que quedan. Sin arte (CLAUDE.md §5), mismo criterio que <c>InventoryScreen</c>/
/// <c>ShopScreen</c> — autocontenido, lee <c>NetClient</c> él solo.
/// <para>
/// Los stats efectivos (con equipo) llegan en <c>EquipmentUpdate</c>, que ya es lo que el servidor
/// manda tras un <c>AllocateStatPoint</c> con éxito (FASE-10 §2 D5): no hace falta un mensaje
/// aparte, sólo pintarlo aquí también.
/// </para>
/// </summary>
public partial class StatsScreen : CanvasLayer
{
    private NetClient _net = null!;
    private Control _root = null!;
    private Label _points = null!;
    private Label _stats = null!;

    /// <inheritdoc />
    public override void _Ready()
    {
        _net = GetNode<NetClient>("/root/NetClient");

        // Canvas base 480x270 (CLAUDE.md §2): con el tema compacto de sobra para 7 filas, pero se
        // deja algo más de alto que el mínimo justo para no repetir el desbordamiento que tenía
        // CharacterSelect.
        var theme = GD.Load<Theme>("res://resources/ui/CompactUiTheme.tres");
        _root = new PanelContainer
        {
            Visible = false,
            Theme = theme,
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -140, OffsetRight = 140, OffsetTop = -125, OffsetBottom = 125,
        };
        AddChild(_root);

        var layout = new VBoxContainer();
        _root.AddChild(layout);
        layout.AddChild(new Label { Text = "Stats (K para cerrar)" });

        _points = new Label();
        layout.AddChild(_points);

        _stats = new Label();
        layout.AddChild(_stats);

        AddStatRow(layout, "Fuerza (STR)", StatKind.Str);
        AddStatRow(layout, "Inteligencia (INT)", StatKind.Int);
        AddStatRow(layout, "Vitalidad (VIT)", StatKind.Vit);
        AddStatRow(layout, "Destreza (DEX)", StatKind.Dex);

        _net.EquipmentUpdateReceived += OnEquipmentUpdate;
    }

    /// <inheritdoc />
    public override void _ExitTree() => _net.EquipmentUpdateReceived -= OnEquipmentUpdate;

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed(World.InputActions.ToggleStats))
        {
            _root.Visible = !_root.Visible;
        }
    }

    private void AddStatRow(VBoxContainer layout, string label, StatKind stat)
    {
        var row = new HBoxContainer();
        layout.AddChild(row);
        row.AddChild(new Label { Text = label, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var button = new Button { Text = "+1" };
        button.Pressed += () => _net.SendAllocateStatPoint(stat);
        row.AddChild(button);
    }

    private void OnEquipmentUpdate(S2CEquipmentUpdate update)
    {
        _points.Text = $"Puntos sin gastar: {update.StatPoints}";
        _stats.Text = $"STR {update.StrEffective}  ·  INT {update.IntEffective}  ·  " +
            $"VIT {update.VitEffective}  ·  DEX {update.DexEffective}";
    }
}
