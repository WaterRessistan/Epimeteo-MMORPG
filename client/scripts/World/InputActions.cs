namespace Epimeteo.Client.World;

/// <summary>
/// Nombres de las acciones del <c>InputMap</c> que se declaran en <c>project.godot</c>. Están aquí
/// para no repartir literales por el código: una acción mal escrita no da error de compilación, se
/// queda callada y el personaje no anda.
/// </summary>
public static class InputActions
{
    /// <summary>Andar hacia el norte (W o flecha arriba).</summary>
    public const string MoveUp = "move_up";

    /// <summary>Andar hacia el sur (S o flecha abajo).</summary>
    public const string MoveDown = "move_down";

    /// <summary>Andar hacia el oeste (A o flecha izquierda).</summary>
    public const string MoveLeft = "move_left";

    /// <summary>Andar hacia el este (D o flecha derecha).</summary>
    public const string MoveRight = "move_right";

    /// <summary>Interactuar con el NPC más cercano (E): abre su tienda, o la cierra si ya está abierta.</summary>
    public const string Interact = "interact";

    /// <summary>Atacar al objetivo más cercano (espacio). El servidor decide si vale (Fase 9).</summary>
    public const string Attack = "attack";

    /// <summary>Lanzar la habilidad del hueco 1 (tecla 1). Fase 10.</summary>
    public const string Skill1 = "skill_1";

    /// <summary>Lanzar la habilidad del hueco 2 (tecla 2). Fase 10.</summary>
    public const string Skill2 = "skill_2";

    /// <summary>Lanzar la habilidad del hueco 3 (tecla 3). Fase 10.</summary>
    public const string Skill3 = "skill_3";

    /// <summary>Alternar el panel de reparto de puntos de stat (tecla K). Fase 10.</summary>
    public const string ToggleStats = "toggle_stats";

    /// <summary>Abrir la caja de chat (Enter). Con la caja abierta, la propia caja captura el Enter para mandar. Fase 11.</summary>
    public const string ChatFocus = "chat_focus";

    /// <summary>Alternar entre canal global y de zona para el próximo mensaje (tecla T). Fase 11.</summary>
    public const string ChatChannelToggle = "chat_channel_toggle";
}
