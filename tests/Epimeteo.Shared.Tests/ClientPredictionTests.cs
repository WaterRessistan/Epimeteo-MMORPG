using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Shared.Tests;

/// <summary>
/// Predicción y reconciliación: lo que separa un juego que responde al instante de uno con goma
/// elástica. Simula al servidor aplicando los mismos inputs con el mismo <see cref="MovementSystem"/>.
/// </summary>
public sealed class ClientPredictionTests
{
    private static MoveInput Right(uint seq) => new(seq, 1, 0, Facing.East);

    [Fact]
    public void ElInputSePredice_SinEsperarAlServidor()
    {
        var map = TestMaps.OpenRoom();
        var prediction = new ClientPrediction(map, MoveState.AtRest(new Vec2(4f, 4f), Facing.South));

        var state = prediction.ApplyInput(Right(1));

        Assert.Equal(4.2f, state.Pos.X, 1e-5f);
        Assert.Equal(1, prediction.PendingCount);
    }

    /// <summary>
    /// El caso normal: el servidor confirma justo lo que se había predicho. Ni una corrección, ni
    /// un tirón, aunque el snapshot llegue con varios inputs de retraso.
    /// </summary>
    [Fact]
    public void SiElServidorConfirmaLoPredicho_NoHayCorreccion()
    {
        var map = TestMaps.OpenRoom();
        var start = MoveState.AtRest(new Vec2(4f, 4f), Facing.South);
        var prediction = new ClientPrediction(map, start);
        var server = start;

        for (uint seq = 1; seq <= 10; seq++)
        {
            prediction.ApplyInput(Right(seq));
            server = MovementSystem.Step(server, Right(seq), map);

            // El servidor va tres inputs por detrás, como con latencia real.
            if (seq > 3)
            {
                Assert.False(prediction.ApplyAuthoritative(server, seq));
            }
        }

        Assert.Equal(0, prediction.Corrections);
        Assert.Equal(0f, prediction.MaxErrorTiles);
    }

    /// <summary>
    /// Y el caso que justifica todo el mecanismo: el servidor dice otra cosa (aquí, una pared que
    /// el cliente no vio). Se acepta su verdad y se reejecutan los inputs pendientes.
    /// </summary>
    [Fact]
    public void SiElServidorDiscrepa_SeCorrigeYSeReejecutanLosPendientes()
    {
        var map = TestMaps.OpenRoom();
        var start = MoveState.AtRest(new Vec2(4f, 4f), Facing.South);
        var prediction = new ClientPrediction(map, start);

        for (uint seq = 1; seq <= 5; seq++)
        {
            prediction.ApplyInput(Right(seq));
        }

        // El servidor confirma el input 2 en una posición distinta de la predicha.
        var authoritative = MoveState.AtRest(new Vec2(3f, 4f), Facing.East);
        Assert.True(prediction.ApplyAuthoritative(authoritative, 2));

        Assert.Equal(1, prediction.Corrections);
        Assert.Equal(3, prediction.PendingCount);

        // 3 + los tres inputs que el servidor aún no había visto.
        Assert.Equal(3f + (3 * 0.2f), prediction.Predicted.Pos.X, 1e-5f);
        Assert.Equal(authoritative, prediction.Confirmed);
    }

    [Fact]
    public void TrasCorregir_LaSiguienteComparacionUsaLaPrediccionNueva()
    {
        var map = TestMaps.OpenRoom();
        var start = MoveState.AtRest(new Vec2(4f, 4f), Facing.South);
        var prediction = new ClientPrediction(map, start);

        for (uint seq = 1; seq <= 5; seq++)
        {
            prediction.ApplyInput(Right(seq));
        }

        prediction.ApplyAuthoritative(MoveState.AtRest(new Vec2(3f, 4f), Facing.East), 2);

        // El servidor sigue desde donde dijo, aplicando los inputs 3 y 4: el cliente ya lo tenía
        // reejecutado igual, así que no debe corregir otra vez.
        var server = MoveState.AtRest(new Vec2(3f, 4f), Facing.East);
        server = MovementSystem.Step(server, Right(3), map);
        server = MovementSystem.Step(server, Right(4), map);

        Assert.False(prediction.ApplyAuthoritative(server, 4));
        Assert.Equal(1, prediction.Corrections);
    }

    [Fact]
    public void UnaDiferenciaMinuscula_NoDisparaCorreccion()
    {
        var map = TestMaps.OpenRoom();
        var start = MoveState.AtRest(new Vec2(4f, 4f), Facing.South);
        var prediction = new ClientPrediction(map, start);
        prediction.ApplyInput(Right(1));

        var casiIgual = prediction.Predicted with { Pos = prediction.Predicted.Pos + new Vec2(0.01f, 0.01f) };

        Assert.False(prediction.ApplyAuthoritative(casiIgual, 1));
        Assert.Equal(0, prediction.Corrections);
    }

    [Fact]
    public void ElBufferNoCreceSinLimite()
    {
        var map = TestMaps.OpenRoom();
        var prediction = new ClientPrediction(map, MoveState.AtRest(new Vec2(4f, 4f), Facing.South));

        for (uint seq = 1; seq <= 500; seq++)
        {
            prediction.ApplyInput(Right(seq));
        }

        Assert.Equal(ClientPrediction.Capacity, prediction.PendingCount);
    }

    /// <summary>
    /// Un cliente y un servidor que aplican los mismos 400 inputs contra el mismo mapa acaban en
    /// el mismo sitio sin una sola corrección. Es la prueba de que la predicción es exacta y no
    /// "aproximada con corrección continua".
    /// </summary>
    [Fact]
    public void CuatrocientosInputsContraUnaPared_TerminanSinNingunaCorreccion()
    {
        var map = TestMaps.OpenRoom();
        var start = MoveState.AtRest(new Vec2(4f, 4f), Facing.South);
        var prediction = new ClientPrediction(map, start);
        var server = start;

        for (uint seq = 1; seq <= 400; seq++)
        {
            var input = new MoveInput(seq, 1, (sbyte)(seq % 3 == 0 ? 1 : 0), Facing.East);
            prediction.ApplyInput(input);
            server = MovementSystem.Step(server, input, map);
            prediction.ApplyAuthoritative(server, seq);
        }

        Assert.Equal(0, prediction.Corrections);
        Assert.Equal(server.Pos, prediction.Predicted.Pos);
    }
}
