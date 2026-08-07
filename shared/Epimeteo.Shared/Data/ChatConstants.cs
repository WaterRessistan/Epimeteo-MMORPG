namespace Epimeteo.Shared.Data;

/// <summary>Números de chat que tienen que valer lo mismo en cliente y servidor (FASE-11 §2 D10).</summary>
public static class ChatConstants
{
    /// <summary>Longitud máxima de <c>ChatSend.Text</c>, en caracteres. Vacío o más largo → payload inválido.</summary>
    public const int MaxMessageLength = 240;
}
