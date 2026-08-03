using Epimeteo.Shared.Net;
using Xunit;

namespace Epimeteo.Shared.Tests;

public sealed class OpcodeTableTests
{
    [Fact]
    public void TodosLosOpcodesDelEnumEstanEnLaTabla()
    {
        foreach (var opcode in Enum.GetValues<Opcode>())
        {
            if (opcode == Opcode.None)
            {
                continue;
            }

            Assert.True(OpcodeTable.TryGet(opcode, out _), $"Falta {opcode} en OpcodeTable.");
        }
    }

    [Fact]
    public void NingunValorDeOpcodeSeRepite()
    {
        var valores = Enum.GetValues<Opcode>().Select(o => (ushort)o).ToArray();

        Assert.Equal(valores.Length, valores.Distinct().Count());
    }

    [Fact]
    public void ElRangoDelOpcodeCoincideConSuDireccion()
    {
        foreach (var spec in OpcodeTable.All)
        {
            var esC2S = (ushort)spec.Opcode < 0x8000;

            Assert.Equal(esC2S, spec.IsClientToServer);

            // Un opcode de servidor nunca puede ser legal como entrada.
            if (!esC2S)
            {
                Assert.Equal(SessionState.None, spec.LegalStates);
                Assert.Equal(OpcodeFamily.ServerOnly, spec.Family);
            }
        }
    }

    [Fact]
    public void HelloSoloEsLegalAlConectar()
    {
        Assert.True(OpcodeTable.IsLegalFromClient(Opcode.Hello, SessionState.Connecting));
        Assert.False(OpcodeTable.IsLegalFromClient(Opcode.Hello, SessionState.Greeted));
        Assert.False(OpcodeTable.IsLegalFromClient(Opcode.Hello, SessionState.InWorld));
    }

    [Fact]
    public void PingEsLegalEnCualquierEstadoVivo()
    {
        SessionState[] estados =
        [
            SessionState.Connecting,
            SessionState.Greeted,
            SessionState.Authenticated,
            SessionState.Loading,
            SessionState.InWorld,
        ];

        foreach (var estado in estados)
        {
            Assert.True(OpcodeTable.IsLegalFromClient(Opcode.Ping, estado));
        }
    }

    [Fact]
    public void NingunOpcodeEsLegalDuranteElCierre()
    {
        foreach (var spec in OpcodeTable.All)
        {
            Assert.False(OpcodeTable.IsLegalFromClient(spec.Opcode, SessionState.Closing));
        }
    }

    [Fact]
    public void LosOpcodesDeJuegoExigenEstarEnElMundo()
    {
        Opcode[] deMundo = [Opcode.InputState, Opcode.InvMove, Opcode.ShopBuy, Opcode.FarmPlant, Opcode.Attack, Opcode.ChatSend];

        foreach (var opcode in deMundo)
        {
            Assert.True(OpcodeTable.IsLegalFromClient(opcode, SessionState.InWorld));
            Assert.False(OpcodeTable.IsLegalFromClient(opcode, SessionState.Authenticated));
            Assert.False(OpcodeTable.IsLegalFromClient(opcode, SessionState.Greeted));
        }
    }

    [Fact]
    public void UnOpcodeInexistenteNuncaEsLegal()
    {
        Assert.False(OpcodeTable.IsLegalFromClient((Opcode)0x7FFF, SessionState.InWorld));
        Assert.False(OpcodeTable.IsLegalFromClient(Opcode.None, SessionState.Connecting));
    }

    [Fact]
    public void LosMensajesDeServidorNoSonAceptablesComoEntrada()
    {
        Assert.False(OpcodeTable.IsLegalFromClient(Opcode.Snapshot, SessionState.InWorld));
        Assert.False(OpcodeTable.IsLegalFromClient(Opcode.CurrencyUpdate, SessionState.InWorld));
    }
}
