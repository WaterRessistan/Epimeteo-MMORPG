using System.Buffers;
using System.Buffers.Binary;
using MessagePack;

namespace Epimeteo.Shared.Net;

/// <summary>
/// Envelope binario del protocolo:
/// <code>[ uint16 opcode little-endian ][ payload MessagePack ]</code>
/// Un frame de WebSocket = un mensaje. El framing de longitud lo aporta el propio WebSocket.
/// </summary>
public static class FrameCodec
{
    /// <summary>Tamaño de la cabecera en bytes (el uint16 del opcode).</summary>
    public const int HeaderBytes = 2;

    /// <summary>Límite duro de un frame entrante. Más grande → desconexión.</summary>
    public const int MaxFrameBytes = 16 * 1024;

    /// <summary>
    /// Opciones de MessagePack del protocolo. <c>UntrustedData</c> limita la profundidad de
    /// anidamiento y evita que un payload malicioso reserve arrays gigantes al deserializar.
    /// </summary>
    public static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);

    /// <summary>Serializa un mensaje completo (cabecera + payload) en un array nuevo.</summary>
    public static byte[] Encode<T>(Opcode opcode, T payload)
    {
        var writer = new ArrayBufferWriter<byte>(128);
        BinaryPrimitives.WriteUInt16LittleEndian(writer.GetSpan(HeaderBytes), (ushort)opcode);
        writer.Advance(HeaderBytes);
        MessagePackSerializer.Serialize(writer, payload, Options);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Lee el opcode de un frame. Falso si el frame es más corto que la cabecera.
    /// No valida que el opcode exista en <see cref="OpcodeTable"/>: de eso se encarga la sesión.
    /// </summary>
    public static bool TryReadOpcode(ReadOnlySpan<byte> frame, out Opcode opcode)
    {
        if (frame.Length < HeaderBytes)
        {
            opcode = Opcode.None;
            return false;
        }

        opcode = (Opcode)BinaryPrimitives.ReadUInt16LittleEndian(frame);
        return true;
    }

    /// <summary>Devuelve la porción de payload de un frame, sin copiar.</summary>
    public static ReadOnlyMemory<byte> PayloadOf(ReadOnlyMemory<byte> frame) => frame[HeaderBytes..];

    /// <summary>
    /// Deserializa el payload de un frame. Devuelve falso ante cualquier payload corrupto,
    /// truncado o con una forma que no encaja: los datos del cliente no son de fiar y un
    /// mensaje ilegible es un error de protocolo, no una excepción que suba por la pila.
    /// </summary>
    public static bool TryDecodePayload<T>(ReadOnlyMemory<byte> frame, out T? payload)
    {
        if (frame.Length < HeaderBytes)
        {
            payload = default;
            return false;
        }

        try
        {
            payload = MessagePackSerializer.Deserialize<T>(PayloadOf(frame), Options);
            return true;
        }
        catch (Exception ex) when (ex is MessagePackSerializationException or EndOfStreamException)
        {
            payload = default;
            return false;
        }
    }
}
