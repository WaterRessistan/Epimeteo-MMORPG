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
}
