using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Xunit;

namespace Epimeteo.Shared.Tests;

public sealed class FrameCodecTests
{
    [Fact]
    public void Encode_EscribeElOpcodeEnLittleEndian()
    {
        var frame = FrameCodec.Encode(Opcode.HelloAck, new S2CHelloAck());

        Assert.Equal(0x01, frame[0]);
        Assert.Equal(0x80, frame[1]);
    }

    [Fact]
    public void CicloCompleto_ConservaTodosLosCampos()
    {
        var original = new S2CHelloAck
        {
            ServerProtocolVersion = 7,
            TickRate = 20,
            SnapshotRate = 10,
            ServerTimeMs = 123_456_789L,
        };

        var frame = FrameCodec.Encode(Opcode.HelloAck, original);

        Assert.True(FrameCodec.TryReadOpcode(frame, out var opcode));
        Assert.Equal(Opcode.HelloAck, opcode);
        Assert.True(FrameCodec.TryDecodePayload<S2CHelloAck>(frame, out var decoded));
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void CicloCompleto_ConservaLosEnumsDelKick()
    {
        var original = new S2CKick
        {
            Reason = KickReason.VersionMismatch,
            Detail = ResultCode.VersionMismatch,
            ServerProtocolVersion = ProtocolVersion.Current,
        };

        var frame = FrameCodec.Encode(Opcode.Kick, original);

        Assert.True(FrameCodec.TryDecodePayload<S2CKick>(frame, out var decoded));
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void TryReadOpcode_FalsoSiElFrameEsMasCortoQueLaCabecera()
    {
        Assert.False(FrameCodec.TryReadOpcode(Array.Empty<byte>(), out _));
        Assert.False(FrameCodec.TryReadOpcode(new byte[] { 0x01 }, out _));
    }

    [Fact]
    public void TryDecodePayload_FalsoConPayloadCorrupto()
    {
        // Cabecera válida, basura detrás: el cliente no es de fiar y esto no puede escalar
        // a excepción.
        var frame = new byte[] { 0x01, 0x00, 0xC1, 0xC1, 0xC1 };

        Assert.False(FrameCodec.TryDecodePayload<C2SHello>(frame, out var payload));
        Assert.Null(payload);
    }

    [Fact]
    public void TryDecodePayload_FalsoConPayloadVacio()
    {
        var frame = new byte[] { 0x01, 0x00 };

        Assert.False(FrameCodec.TryDecodePayload<C2SHello>(frame, out _));
    }

    [Fact]
    public void HelloDelCliente_CabeDeSobraEnElLimiteDeFrame()
    {
        var frame = FrameCodec.Encode(Opcode.Hello, new C2SHello
        {
            ProtocolVersion = ProtocolVersion.Current,
            ClientBuild = "0.1.0-dev",
        });

        Assert.InRange(frame.Length, 3, FrameCodec.MaxFrameBytes);
    }
}
