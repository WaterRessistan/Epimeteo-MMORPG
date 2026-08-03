using Epimeteo.Client.Net;
using Godot;

namespace Epimeteo.Client.Ui;

/// <summary>
/// Pantalla placeholder tras <c>WorldEnter</c>. El mundo real —mapa, movimiento, otras
/// entidades visibles— es la Fase 4; esta pantalla sólo confirma que el flujo de personajes
/// terminó bien (mismo criterio que usó la pantalla de login en la Fase 2) y manda
/// <c>WorldReady</c> para que la sesión pase a <c>InWorld</c>.
/// </summary>
public partial class WorldPlaceholderScreen : Control
{
    private NetClient _net = null!;

    public override void _Ready()
    {
        _net = GetNode<NetClient>("/root/NetClient");
        var infoLabel = GetNode<Label>("Layout/Info");

        var enter = _net.LastWorldEnter;
        infoLabel.Text = enter is null
            ? "Sin datos de WorldEnter"
            : $"En {enter.MapKey} — nivel {enter.Stats.Level}, HP {enter.Stats.Hp}, MP {enter.Stats.Mp}, oro {enter.Stats.Gold}";

        _net.SendWorldReady();
    }
}
