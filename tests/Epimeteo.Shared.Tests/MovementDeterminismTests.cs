using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Shared.Tests;

/// <summary>
/// Los dos tests de los que depende toda la Fase 4: el movimiento tiene que ser reproducible
/// (o cliente y servidor divergen) y reejecutable (o la reconciliación no funciona).
/// </summary>
public sealed class MovementDeterminismTests
{
    /// <summary>
    /// Huella del estado tras 10.000 pasos con una secuencia fija de inputs. Está clavada a mano:
    /// si un refactor la cambia, es que el movimiento ya no produce los mismos números y todos los
    /// clientes desplegados quedarían desincronizados. Cambiarla exige subir
    /// <c>ProtocolVersion</c> y desplegar cliente y servidor a la vez.
    /// </summary>
    private const uint ExpectedFingerprint = 0x3078E289;

    [Fact]
    public void DiezMilPasos_ProducenSiempreLaMismaHuella()
    {
        var fingerprint = RunScriptedRun(10_000);

        Assert.Equal(ExpectedFingerprint, fingerprint);
    }

    /// <summary>
    /// Reconciliación en estado puro: simular N pasos de una vez tiene que dar exactamente lo
    /// mismo que simular M y reejecutar los N−M restantes desde el estado intermedio. Bit a bit,
    /// no "parecido": el cliente hace justo esto cada vez que llega un snapshot con error.
    /// </summary>
    [Fact]
    public void ReejecutarLosInputsPendientes_DaElMismoResultadoQueSimularDeSeguido()
    {
        var map = TestMaps.OpenRoom();
        var inputs = BuildInputs(500);
        var start = MoveState.AtRest(new Vec2(4f, 4f), Facing.South);

        var direct = start;
        foreach (var input in inputs)
        {
            direct = MovementSystem.Step(direct, input, map);
        }

        var partial = start;
        for (var i = 0; i < 313; i++)
        {
            partial = MovementSystem.Step(partial, inputs[i], map);
        }

        var replayed = partial;
        for (var i = 313; i < inputs.Length; i++)
        {
            replayed = MovementSystem.Step(replayed, inputs[i], map);
        }

        Assert.Equal(direct, replayed);
        Assert.Equal(Fingerprint(direct), Fingerprint(replayed));
    }

    [Fact]
    public void ElMismoInputDesdeElMismoEstado_NoDependeDeCuantasVecesSeLlame()
    {
        var map = TestMaps.OpenRoom();
        var state = MoveState.AtRest(new Vec2(3.25f, 5.75f), Facing.West);
        var input = new MoveInput(42, 1, -1, Facing.North);

        var first = MovementSystem.Step(state, input, map);
        var second = MovementSystem.Step(state, input, map);

        Assert.Equal(first, second);
    }

    private static uint RunScriptedRun(int steps)
    {
        var map = TestMaps.OpenRoom();
        var state = MoveState.AtRest(new Vec2(4f, 4f), Facing.South);

        foreach (var input in BuildInputs(steps))
        {
            state = MovementSystem.Step(state, input, map);
        }

        return Fingerprint(state);
    }

    /// <summary>
    /// Secuencia de inputs pseudoaleatoria pero fijada: un LCG de 32 bits escrito aquí en vez de
    /// <see cref="Random"/> porque la implementación de <see cref="Random"/> puede cambiar entre
    /// versiones de .NET y con ella la huella esperada.
    /// </summary>
    private static MoveInput[] BuildInputs(int count)
    {
        var inputs = new MoveInput[count];
        var seed = 0x1234_5678u;

        for (var i = 0; i < count; i++)
        {
            seed = (seed * 1664525u) + 1013904223u;
            var dirX = (sbyte)(((seed >> 16) % 3) - 1);
            var dirY = (sbyte)(((seed >> 20) % 3) - 1);
            var facing = (Facing)((seed >> 24) % 4);
            inputs[i] = new MoveInput((uint)i + 1, dirX, dirY, facing);
        }

        return inputs;
    }

    /// <summary>FNV-1a sobre los bits exactos del estado: cualquier diferencia de un ULP se ve.</summary>
    private static uint Fingerprint(MoveState state)
    {
        var hash = 2166136261u;

        Mix(ref hash, (uint)BitConverter.SingleToInt32Bits(state.Pos.X));
        Mix(ref hash, (uint)BitConverter.SingleToInt32Bits(state.Pos.Y));
        Mix(ref hash, (uint)BitConverter.SingleToInt32Bits(state.Vel.X));
        Mix(ref hash, (uint)BitConverter.SingleToInt32Bits(state.Vel.Y));
        Mix(ref hash, (uint)state.Facing);
        Mix(ref hash, (uint)state.Anim);

        return hash;
    }

    private static void Mix(ref uint hash, uint value)
    {
        for (var i = 0; i < 4; i++)
        {
            hash = (hash ^ (byte)(value >> (i * 8))) * 16777619u;
        }
    }
}
