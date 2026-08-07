using Epimeteo.Shared.Simulation;
using Godot;

namespace Epimeteo.Client.Ui;

/// <summary>
/// El panel de diagnóstico del mundo. No es decoración: mientras no haya arte, <b>el HUD es el
/// instrumento con el que se comprueba la fase</b> (FASE-04 §7). Correcciones a 0 y error máximo
/// por debajo de 0,05 tiles con latencia es exactamente lo que el criterio de aceptación pide, y
/// aquí se lee sin herramientas.
/// </summary>
public partial class WorldHud : Control
{
    private static readonly Color Safe = new(0.65f, 0.85f, 0.65f);
    private static readonly Color Hostile = new(1f, 0.45f, 0.4f);
    private static readonly Color Normal = new(0.9f, 0.9f, 0.92f);

    private Label _position = null!;
    private Label _combat = null!;
    private Label _network = null!;
    private Label _region = null!;
    private Label _prediction = null!;
    private Label _skills = null!;

    /// <inheritdoc />
    public override void _Ready()
    {
        _position = GetNode<Label>("Panel/Lines/Position");
        _network = GetNode<Label>("Panel/Lines/Network");
        _region = GetNode<Label>("Panel/Lines/Region");
        _prediction = GetNode<Label>("Panel/Lines/Prediction");
        _combat = GetNode<Label>("Panel/Lines/Combat");
        _skills = GetNode<Label>("Panel/Lines/Skills");
    }

    /// <summary>Posición predicha del jugador y entidades visibles.</summary>
    public void SetPosition(Vec2 pos, int visibleEntities) =>
        _position.Text = $"({pos.X:F2}, {pos.Y:F2})  ·  {visibleEntities} a la vista";

    /// <summary>RTT medido y latencia simulada, si la hay.</summary>
    public void SetNetwork(long rttMs, int simulatedLagMs)
    {
        var rtt = rttMs < 0 ? "—" : $"{rttMs} ms";
        _network.Text = simulatedLagMs > 0
            ? $"RTT {rtt}  ·  lag simulado {simulatedLagMs} ms/sentido"
            : $"RTT {rtt}";
    }

    /// <summary>
    /// Región y flags <b>según el servidor</b>. El cliente calcula su región para enseñarla al
    /// instante, pero quien decide si se puede atacar es el servidor (CLAUDE.md §4).
    /// </summary>
    public void SetRegion(string regionName, ZoneFlags flags)
    {
        var name = string.IsNullOrEmpty(regionName) ? "sin región" : regionName;
        var hostile = flags.HasFlag(ZoneFlags.Pvp);

        _region.Text = hostile ? $"{name} — ZONA HOSTIL" : name;
        _region.AddThemeColorOverride("font_color", hostile ? Hostile : Safe);
    }

    /// <summary>Correcciones acumuladas y peor error. Con 0 ms de latencia tienen que quedarse en cero.</summary>
    /// <summary>
    /// Vida, nivel/XP, objetivo y flag de combate (Fase 9, nivel/XP ampliado en Fase 10). Sin arte:
    /// es el mismo criterio que el resto del HUD — números, que es lo que hace falta para verificar.
    /// </summary>
    public void SetCombat(int hp, int hpMax, int level, long xp, long xpToNextLevel, string targetLabel, bool inCombat)
    {
        _combat.Text = $"HP {hp}/{hpMax}  ·  Nv {level} ({xp}/{xpToNextLevel} XP)  ·  {targetLabel}";
        _combat.AddThemeColorOverride("font_color", inCombat ? Hostile : Normal);
    }

    public void SetPrediction(int corrections, float maxErrorTiles, int pendingInputs) =>
        _prediction.Text = $"corr {corrections}  ·  err máx {maxErrorTiles:F3} t  ·  pend {pendingInputs}";

    /// <summary>
    /// Barra de habilidades (Fase 10): tecla, clave, y cooldown restante en segundos calculado por
    /// el cliente de forma optimista — el servidor manda <c>SkillNotUnlocked</c>/<c>OnCooldown</c>
    /// si al final no valía (§7 del plan de fase).
    /// </summary>
    public void SetSkills(string text) => _skills.Text = text;

    /// <summary>Mensaje de error a pantalla completa. Se usa si el contenido no cuadra con el servidor.</summary>
    public void ShowFatal(string message)
    {
        _position.Text = message;
        _position.AddThemeColorOverride("font_color", Hostile);
        _network.Text = string.Empty;
        _region.Text = string.Empty;
        _prediction.Text = string.Empty;
        _skills.Text = string.Empty;
    }
}
