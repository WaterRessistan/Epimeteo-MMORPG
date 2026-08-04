using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Shared.Tests;

/// <summary>
/// El sistema de movimiento es el código que ejecutan cliente y servidor por igual: si estos
/// tests no están en verde, la predicción no puede coincidir con la autoridad.
/// </summary>
public sealed class MovementSystemTests
{
    private const float Step = SimulationConstants.WalkSpeedTilesPerSec * SimulationConstants.TickDt;
    private const float Tolerance = 1e-5f;

    [Theory]
    [InlineData(1, 0, Step, 0f, Facing.East)]
    [InlineData(-1, 0, -Step, 0f, Facing.West)]
    [InlineData(0, 1, 0f, Step, Facing.South)]
    [InlineData(0, -1, 0f, -Step, Facing.North)]
    public void AndarEnUnEje_AvanzaUnPasoYMiraHaciaAlli(
        int dirX, int dirY, float expectedDx, float expectedDy, Facing expectedFacing)
    {
        var map = TestMaps.OpenRoom();
        var state = MoveState.AtRest(new Vec2(4f, 4f), Facing.South);

        var result = MovementSystem.Step(state, new MoveInput(1, (sbyte)dirX, (sbyte)dirY, Facing.South), map);

        Assert.Equal(4f + expectedDx, result.Pos.X, Tolerance);
        Assert.Equal(4f + expectedDy, result.Pos.Y, Tolerance);
        Assert.Equal(expectedFacing, result.Facing);
        Assert.Equal(AnimState.Walk, result.Anim);
    }

    /// <summary>
    /// El bug clásico de los juegos top-down: en diagonal se recorre √2 veces más distancia y el
    /// jugador aprende a moverse siempre en diagonal. La constante <c>DiagonalFactor</c> existe
    /// para esto.
    /// </summary>
    [Fact]
    public void EnDiagonal_SeRecorreLaMismaDistanciaQueEnRecto()
    {
        var map = TestMaps.OpenRoom();
        var state = MoveState.AtRest(new Vec2(4f, 4f), Facing.South);

        var result = MovementSystem.Step(state, new MoveInput(1, 1, 1, Facing.South), map);

        var dx = result.Pos.X - 4f;
        var dy = result.Pos.Y - 4f;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));

        Assert.Equal(Step, distance, 1e-4);
    }

    [Fact]
    public void SinDireccion_NoSeMueveYConservaLaOrientacion()
    {
        var map = TestMaps.OpenRoom();
        var state = new MoveState(new Vec2(4f, 4f), new Vec2(4f, 0f), Facing.East, AnimState.Walk);

        var result = MovementSystem.Step(state, MoveInput.Idle(1, Facing.East), map);

        Assert.Equal(state.Pos, result.Pos);
        Assert.Equal(Facing.East, result.Facing);
        Assert.Equal(AnimState.Idle, result.Anim);
        Assert.Equal(Vec2.Zero, result.Vel);
    }

    [Fact]
    public void ContraUnaPared_SePegaAlBordeYNoLaAtraviesa()
    {
        var map = TestMaps.OpenRoom();
        var state = MoveState.AtRest(new Vec2(6.5f, 4f), Facing.East);

        for (var i = 0u; i < 50; i++)
        {
            state = MovementSystem.Step(state, new MoveInput(i, 1, 0, Facing.East), map);
        }

        // El muro derecho es la columna 7; el jugador queda con su borde justo en x = 7.
        Assert.Equal(7f - SimulationConstants.PlayerHalfWidth, state.Pos.X, Tolerance);
        Assert.False(map.IsBlocked(state.Pos, SimulationConstants.PlayerHalfWidth, SimulationConstants.PlayerHalfHeight));
    }

    /// <summary>
    /// Empujar contra la pared 10.000 ticks (8 minutos de juego) sin acabar dentro de un sólido:
    /// es la prueba de que no hay túnel ni acumulación de error.
    /// </summary>
    [Fact]
    public void EmpujandoContraLaPared_NuncaSeQuedaDentroDeUnSolido()
    {
        var map = TestMaps.OpenRoom();
        var state = MoveState.AtRest(new Vec2(4f, 4f), Facing.South);

        for (var i = 0u; i < 10_000; i++)
        {
            var dirX = (sbyte)(i % 2 == 0 ? 1 : 0);
            var dirY = (sbyte)(i % 3 == 0 ? 1 : 0);
            state = MovementSystem.Step(state, new MoveInput(i, dirX, dirY, Facing.South), map);

            Assert.False(
                map.IsBlocked(state.Pos, SimulationConstants.PlayerHalfWidth, SimulationConstants.PlayerHalfHeight),
                $"Posición dentro de un sólido en el tick {i}: {state.Pos}");
        }
    }

    /// <summary>
    /// Deslizamiento: si X choca pero Y está libre, la entidad se desliza por la pared. Sin esto,
    /// caminar en diagonal contra una pared deja al jugador clavado.
    /// </summary>
    [Fact]
    public void EnDiagonalContraUnaPared_SeDeslizaPorElEjeLibre()
    {
        var map = TestMaps.OpenRoom();
        var state = MoveState.AtRest(new Vec2(6.5f, 4f), Facing.East);

        var result = MovementSystem.Step(state, new MoveInput(1, 1, 1, Facing.East), map);

        Assert.Equal(7f - SimulationConstants.PlayerHalfWidth, result.Pos.X, Tolerance);
        Assert.True(result.Pos.Y > 4f, "El eje libre tiene que seguir avanzando.");
    }

    [Fact]
    public void UnPasilloDeUnTile_EsTransitable()
    {
        var map = TestMaps.From(
            "#####",
            "##.##",
            "##.##",
            "##.##",
            "#####");

        var state = MoveState.AtRest(new Vec2(2.5f, 3.5f), Facing.North);

        for (var i = 0u; i < 20; i++)
        {
            state = MovementSystem.Step(state, new MoveInput(i, 0, -1, Facing.North), map);
        }

        Assert.Equal(2.5f, state.Pos.X, Tolerance);
        Assert.Equal(1f + SimulationConstants.PlayerHalfHeight, state.Pos.Y, Tolerance);
    }

    /// <summary>
    /// Un pilar en diagonal: el jugador va hacia (3.5, 3.5) y el tile (2,2) está en medio. No debe
    /// atravesar el vértice —el fallo clásico de resolver la colisión sólo por el centro— pero sí
    /// debe poder rodearlo, porque los tiles ortogonales están libres.
    /// </summary>
    [Fact]
    public void ConUnPilarEnDiagonal_LoRodeaSinAtravesarElVertice()
    {
        var map = TestMaps.From(
            "#####",
            "#...#",
            "#.#.#",
            "#...#",
            "#####");

        var state = MoveState.AtRest(new Vec2(1.5f, 1.5f), Facing.South);

        for (var i = 0u; i < 100; i++)
        {
            state = MovementSystem.Step(state, new MoveInput(i, 1, 1, Facing.South), map);
            Assert.False(
                map.IsBlocked(state.Pos, SimulationConstants.PlayerHalfWidth, SimulationConstants.PlayerHalfHeight),
                $"Atravesó geometría sólida en el tick {i}: {state.Pos}");
        }

        Assert.True(state.Pos.X > 2.5f && state.Pos.Y > 2.5f,
            $"Debería haber rodeado el pilar y llegado a la esquina opuesta, pero acabó en {state.Pos}.");
    }

    [Fact]
    public void ChocarDeFrente_DejaLaAnimacionEnIdle()
    {
        var map = TestMaps.OpenRoom();
        var state = MoveState.AtRest(new Vec2(7f - SimulationConstants.PlayerHalfWidth, 4f), Facing.East);

        var result = MovementSystem.Step(state, new MoveInput(1, 1, 0, Facing.East), map);

        Assert.Equal(state.Pos, result.Pos);
        Assert.Equal(AnimState.Idle, result.Anim);
        Assert.Equal(Facing.East, result.Facing);
    }

    [Fact]
    public void LaVelocidad_EsElDesplazamientoRealDelPaso()
    {
        var map = TestMaps.OpenRoom();
        var state = MoveState.AtRest(new Vec2(4f, 4f), Facing.South);

        var result = MovementSystem.Step(state, new MoveInput(1, 1, 0, Facing.East), map);

        Assert.Equal(SimulationConstants.WalkSpeedTilesPerSec, result.Vel.X, 1e-4f);
        Assert.Equal(0f, result.Vel.Y, Tolerance);
    }
}
