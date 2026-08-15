using System.Collections.Generic;
using Epimeteo.Shared.Net.Messages;
using Godot;

namespace Epimeteo.Client.Ui;

/// <summary>Lo que hay puesto en un hueco de equipo, ya resuelto a texto — <c>WorldScreen</c> conoce el <c>ItemCatalog</c>, <c>CombatHud</c> no necesita saber de contenido.</summary>
public readonly record struct WeaponSlotInfo(string DisplayName, string DefKey);

/// <summary>Un hueco de la barra de habilidades, ya resuelto para pintar (icono, cooldown en 0..1, bloqueo por nivel).</summary>
public readonly record struct SkillSlotInfo(string DisplayName, CombatEventKind Kind, bool Locked, int RequiredLevel, float CooldownFraction);

/// <summary>
/// El HUD de combate de verdad (pedido por Mario: "mucho mejor el combate", indicador de arma,
/// mejor indicador de salud). Vive aparte de <see cref="WorldHud"/> a propósito: ese panel sigue
/// siendo el instrumento de diagnóstico con el que se verificaron las Fases 4-9 (corr/err/pend son
/// literalmente los números del criterio de aceptación de la Fase 4) y no había que tocarlo para
/// no perder esa herramienta; este es puramente para jugar.
/// <para>
/// Autocontenido y construido en código, mismo criterio que <c>InventoryScreen</c>/<c>ShopScreen</c>/
/// <c>StatsScreen</c>: sin <c>.tscn</c> propio, sólo el nodo raíz en <c>World.tscn</c>.
/// </para>
/// </summary>
public partial class CombatHud : CanvasLayer
{
    private const string SwordIconPath = "res://assets/sprites/ui/kenney_roguelike_characters/weapon_sword.png";
    private const string ShieldIconPath = "res://assets/sprites/ui/kenney_roguelike_characters/weapon_shield.png";
    private const string HealIconPath = "res://assets/sprites/ui/kenney_roguelike_characters/skill_heal.png";

    private static readonly Color HpColor = new(0.82f, 0.22f, 0.22f);
    private static readonly Color HpLowColor = new(0.95f, 0.65f, 0.15f);
    private static readonly Color MpColor = new(0.25f, 0.5f, 0.9f);
    private static readonly Color XpColor = new(0.85f, 0.7f, 0.2f);
    private static readonly Color BarBg = new(0.08f, 0.08f, 0.1f, 0.9f);
    private static readonly Color EmptySlot = new(1f, 1f, 1f, 0.25f);
    private static readonly Color CooldownOverlay = new(0f, 0f, 0f, 0.72f);
    private static readonly Color LockedOverlay = new(0.05f, 0.05f, 0.08f, 0.75f);

    private Texture2D? _swordIcon;
    private Texture2D? _shieldIcon;
    private Texture2D? _healIcon;

    private Label _levelLabel = null!;
    private ProgressBar _hpBar = null!;
    private Label _hpLabel = null!;
    private ProgressBar _mpBar = null!;
    private Label _mpLabel = null!;
    private ProgressBar _xpBar = null!;

    private TextureRect _mainHandIcon = null!;
    private Label _mainHandLabel = null!;
    private TextureRect _offHandIcon = null!;

    private Control _targetFrame = null!;
    private Label _targetName = null!;
    private ProgressBar _targetHpBar = null!;

    private Label _toast = null!;
    private double _toastRemainingS;

    private readonly List<SkillCell> _skillCells = [];

    private sealed class SkillCell
    {
        public required Control Root;
        public required TextureRect Icon;
        public required Label Key;
        public required ColorRect Cooldown;
        public required Label Status;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _swordIcon = LoadOptional(SwordIconPath);
        _shieldIcon = LoadOptional(ShieldIconPath);
        _healIcon = LoadOptional(HealIconPath);

        var theme = GD.Load<Theme>("res://resources/ui/CompactUiTheme.tres");

        BuildVitals(theme);
        BuildTargetFrame(theme);
        BuildSkillBar(theme);
        BuildToast();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_toastRemainingS <= 0)
        {
            return;
        }

        _toastRemainingS -= delta;
        const double FadeOutS = 0.4;
        var alpha = (float)Mathf.Clamp(_toastRemainingS / FadeOutS, 0.0, 1.0);
        _toast.Modulate = new Color(1f, 1f, 1f, alpha);
        if (_toastRemainingS <= 0)
        {
            _toast.Text = string.Empty;
        }
    }

    private void BuildToast()
    {
        _toast = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0f, AnchorBottom = 0f,
            OffsetLeft = -100, OffsetRight = 100, OffsetTop = 34, OffsetBottom = 50,
        };
        _toast.AddThemeFontSizeOverride("font_size", 10);
        _toast.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.5f));
        _toast.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.8f));
        _toast.AddThemeConstantOverride("shadow_offset_x", 1);
        _toast.AddThemeConstantOverride("shadow_offset_y", 1);
        AddChild(_toast);
    }

    /// <summary>
    /// Por qué falló el último ataque/habilidad, tal como lo cuenta el servidor
    /// (<c>S2CSystemMessage</c> con prefijo <c>combat.</c>) — antes de esta sesión ese mensaje
    /// llegaba pero no se enseñaba en ningún sitio visible durante el combate, así que un rechazo
    /// (cooldown, fuera de alcance, zona segura…) se sentía como que el ataque no había hecho nada.
    /// </summary>
    public void ShowCombatMessage(string text)
    {
        _toast.Text = text;
        _toastRemainingS = 1.8;
    }

    private static Texture2D? LoadOptional(string path) => ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;

    private void BuildVitals(Theme theme)
    {
        var panel = new PanelContainer
        {
            Theme = theme,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0f, AnchorTop = 1f, AnchorRight = 0f, AnchorBottom = 1f,
            OffsetLeft = 6, OffsetTop = -96, OffsetRight = 150, OffsetBottom = -6,
        };
        AddChild(panel);

        var root = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddChild(root);

        var bars = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(96, 0),
        };
        root.AddChild(bars);

        _levelLabel = new Label();
        bars.AddChild(_levelLabel);

        _hpBar = NewBar(bars, HpColor, 8, out _hpLabel);
        _mpBar = NewBar(bars, MpColor, 6, out _mpLabel);
        _xpBar = NewBar(bars, XpColor, 3, out _, showLabel: false);

        var weapons = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(38, 0),
        };
        root.AddChild(weapons);

        _mainHandIcon = NewIcon(weapons);
        _mainHandLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(38, 0),
        };
        _mainHandLabel.AddThemeFontSizeOverride("font_size", 8);
        weapons.AddChild(_mainHandLabel);

        _offHandIcon = NewIcon(weapons);

        var swapHint = new Label { Text = "[Q] cambiar" };
        swapHint.AddThemeFontSizeOverride("font_size", 7);
        swapHint.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.55f));
        weapons.AddChild(swapHint);
    }

    private static TextureRect NewIcon(Control parent)
    {
        var icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(16, 16),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SelfModulate = EmptySlot,
        };
        parent.AddChild(icon);
        return icon;
    }

    private static ProgressBar NewBar(VBoxContainer parent, Color fill, int height, out Label label, bool showLabel = true)
    {
        var bar = new ProgressBar
        {
            CustomMinimumSize = new Vector2(0, height),
            ShowPercentage = false,
            MaxValue = 1,
        };
        bar.AddThemeStyleboxOverride("background", Flat(BarBg));
        bar.AddThemeStyleboxOverride("fill", Flat(fill));
        parent.AddChild(bar);

        label = new Label { Visible = showLabel };
        if (showLabel)
        {
            label.AddThemeFontSizeOverride("font_size", 8);
            parent.AddChild(label);
        }

        return bar;
    }

    private static StyleBoxFlat Flat(Color color) => new()
    {
        BgColor = color,
        CornerRadiusTopLeft = 1, CornerRadiusTopRight = 1, CornerRadiusBottomLeft = 1, CornerRadiusBottomRight = 1,
    };

    private void BuildTargetFrame(Theme theme)
    {
        _targetFrame = new PanelContainer
        {
            Theme = theme,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0f, AnchorBottom = 0f,
            OffsetLeft = -55, OffsetRight = 55, OffsetTop = 6, OffsetBottom = 30,
        };
        AddChild(_targetFrame);

        var layout = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _targetFrame.AddChild(layout);

        _targetName = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _targetName.AddThemeFontSizeOverride("font_size", 9);
        layout.AddChild(_targetName);

        _targetHpBar = new ProgressBar { CustomMinimumSize = new Vector2(0, 6), ShowPercentage = false, MaxValue = 1 };
        _targetHpBar.AddThemeStyleboxOverride("background", Flat(BarBg));
        _targetHpBar.AddThemeStyleboxOverride("fill", Flat(HpColor));
        layout.AddChild(_targetHpBar);
    }

    private void BuildSkillBar(Theme theme)
    {
        var row = new HBoxContainer
        {
            Theme = theme,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 1f, AnchorBottom = 1f,
            OffsetTop = -30, OffsetBottom = -6,
        };
        row.AddThemeConstantOverride("separation", 4);
        AddChild(row);

        for (var i = 0; i < 3; i++)
        {
            var cellRoot = new Control { CustomMinimumSize = new Vector2(24, 24), MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddChild(cellRoot);

            var bg = new ColorRect { Size = new Vector2(24, 24), Color = new Color(0.08f, 0.08f, 0.1f, 0.85f) };
            cellRoot.AddChild(bg);

            var icon = new TextureRect
            {
                Position = new Vector2(4, 4),
                Size = new Vector2(16, 16),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            };
            cellRoot.AddChild(icon);

            var cooldown = new ColorRect { Color = CooldownOverlay, Position = new Vector2(0, 24), Size = new Vector2(24, 0) };
            cellRoot.AddChild(cooldown);

            var key = new Label { Text = (i + 1).ToString(), Position = new Vector2(1, 0) };
            key.AddThemeFontSizeOverride("font_size", 7);
            key.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.7f));
            cellRoot.AddChild(key);

            var status = new Label { Position = new Vector2(0, 24), HorizontalAlignment = HorizontalAlignment.Center, Size = new Vector2(24, 10) };
            status.AddThemeFontSizeOverride("font_size", 7);
            status.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
            cellRoot.AddChild(status);

            _skillCells.Add(new SkillCell { Root = cellRoot, Icon = icon, Key = key, Cooldown = cooldown, Status = status });
        }

        row.OffsetLeft = -(3 * 24 + 2 * 4) / 2;
        row.OffsetRight = (3 * 24 + 2 * 4) / 2;
    }

    /// <summary>Vida, maná y experiencia. La vida cambia a un tono de aviso por debajo del 30%.</summary>
    public void SetVitals(int hp, int hpMax, int mp, int mpMax, int level, long xp, long xpToNext)
    {
        _levelLabel.Text = $"Nv {level}";

        _hpBar.MaxValue = System.Math.Max(hpMax, 1);
        _hpBar.Value = hp;
        _hpLabel.Text = $"{hp}/{hpMax}";
        var lowHp = hpMax > 0 && hp <= hpMax * 0.3;
        _hpBar.AddThemeStyleboxOverride("fill", Flat(lowHp ? HpLowColor : HpColor));

        _mpBar.MaxValue = System.Math.Max(mpMax, 1);
        _mpBar.Value = mp;
        _mpLabel.Text = $"{mp}/{mpMax}";

        _xpBar.MaxValue = System.Math.Max(xpToNext, 1);
        _xpBar.Value = xp;
    }

    /// <summary>Arma en mano principal y secundaria (escudo…), o <c>null</c> con el hueco vacío.</summary>
    public void SetWeapon(WeaponSlotInfo? mainHand, WeaponSlotInfo? offHand)
    {
        if (mainHand is { } main)
        {
            _mainHandIcon.Texture = _swordIcon;
            _mainHandIcon.SelfModulate = Colors.White;
            _mainHandLabel.Text = main.DisplayName;
        }
        else
        {
            _mainHandIcon.Texture = _swordIcon;
            _mainHandIcon.SelfModulate = EmptySlot;
            _mainHandLabel.Text = "Sin arma";
        }

        _offHandIcon.Texture = _shieldIcon;
        _offHandIcon.SelfModulate = offHand is null ? EmptySlot : Colors.White;
    }

    /// <summary>
    /// Objetivo actual (el más cercano dentro del radio ancho de "quién tengo cerca" del HUD, no
    /// necesariamente al alcance de un golpe), o <c>null</c> para ocultar el marco.
    /// <paramref name="inMeleeRange"/> es la respuesta a "si pulso atacar ahora mismo, ¿va a
    /// valer?" — antes de esta sesión la única forma de saberlo era intentarlo y ver si llegaba un
    /// rechazo; ahora el nombre se atenúa y avisa en cuanto hay que acercarse un poco más.
    /// </summary>
    public void SetTarget(string? name, int hp, int hpMax, bool inMeleeRange)
    {
        _targetFrame.Visible = name is not null;
        if (name is null)
        {
            return;
        }

        _targetName.Text = inMeleeRange ? name : $"{name} (acércate)";
        _targetName.AddThemeColorOverride("font_color", inMeleeRange ? Colors.White : new Color(1f, 1f, 1f, 0.55f));
        _targetHpBar.MaxValue = System.Math.Max(hpMax, 1);
        _targetHpBar.Value = hp;
    }

    /// <summary>Los 3 huecos de habilidad: icono según el tipo, cooldown como una cortina que se retira, y bloqueo por nivel.</summary>
    public void SetSkills(IReadOnlyList<SkillSlotInfo> slots)
    {
        for (var i = 0; i < _skillCells.Count; i++)
        {
            var cell = _skillCells[i];
            if (i >= slots.Count)
            {
                cell.Root.Visible = false;
                continue;
            }

            cell.Root.Visible = true;
            var slot = slots[i];
            cell.Icon.Texture = slot.Kind == CombatEventKind.Heal ? _healIcon : _swordIcon;

            if (slot.Locked)
            {
                cell.Icon.SelfModulate = new Color(1f, 1f, 1f, 0.3f);
                cell.Cooldown.Color = LockedOverlay;
                cell.Cooldown.Position = new Vector2(0, 0);
                cell.Cooldown.Size = new Vector2(24, 24);
                cell.Status.Text = $"Nv{slot.RequiredLevel}";
                cell.Status.Visible = true;
                continue;
            }

            cell.Icon.SelfModulate = Colors.White;
            cell.Cooldown.Color = CooldownOverlay;
            var coveredHeight = 24f * slot.CooldownFraction;
            cell.Cooldown.Position = new Vector2(0, 24 - coveredHeight);
            cell.Cooldown.Size = new Vector2(24, coveredHeight);
            cell.Status.Visible = false;
        }
    }
}
