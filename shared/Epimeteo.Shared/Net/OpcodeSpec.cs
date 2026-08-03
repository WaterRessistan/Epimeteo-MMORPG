namespace Epimeteo.Shared.Net;

/// <summary>
/// Metadatos de un opcode: a qué familia pertenece y en qué estados de sesión es legal recibirlo.
/// </summary>
/// <param name="Opcode">El opcode descrito.</param>
/// <param name="Family">Familia para el rate limiter.</param>
/// <param name="LegalStates">
/// Máscara de estados en los que el servidor acepta este mensaje. Para los opcodes S2C es
/// <see cref="SessionState.None"/>: recibirlos del cliente siempre es ilegal.
/// </param>
public readonly record struct OpcodeSpec(Opcode Opcode, OpcodeFamily Family, SessionState LegalStates)
{
    /// <summary>Verdadero si el opcode va del cliente al servidor (rango 0x0000–0x7FFF).</summary>
    public bool IsClientToServer => (ushort)Opcode < 0x8000;
}
