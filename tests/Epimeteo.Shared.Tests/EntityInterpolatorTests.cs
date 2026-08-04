using Epimeteo.Shared.Simulation;
using Xunit;

namespace Epimeteo.Shared.Tests;

/// <summary>
/// La interpolación de las entidades remotas: lo que decide si los demás jugadores se ven andar
/// o dando saltos. No necesita Godot, y por eso puede comprobarse aquí.
/// </summary>
public sealed class EntityInterpolatorTests
{
    private static MoveState At(float x, float y, Facing facing = Facing.South, AnimState anim = AnimState.Walk) =>
        new(new Vec2(x, y), Vec2.Zero, facing, anim);

    [Fact]
    public void SinMuestrasSeQuedaEnLaPoseInicial()
    {
        var interpolator = new EntityInterpolator(At(3f, 4f));

        interpolator.Interpolate(100);

        Assert.Equal(new Vec2(3f, 4f), interpolator.Current.Pos);
    }

    [Fact]
    public void InterpolaAMitadDeCamino()
    {
        var interpolator = new EntityInterpolator(At(0f, 0f));
        interpolator.PushSample(10, At(10f, 20f));
        interpolator.PushSample(20, At(20f, 40f));

        interpolator.Interpolate(15);

        Assert.Equal(15f, interpolator.Current.Pos.X, 4);
        Assert.Equal(30f, interpolator.Current.Pos.Y, 4);
    }

    [Fact]
    public void ConVariosTramosEligeElQueContieneElInstante()
    {
        var interpolator = new EntityInterpolator(At(0f, 0f));
        interpolator.PushSample(10, At(0f, 0f));
        interpolator.PushSample(20, At(10f, 0f));
        interpolator.PushSample(30, At(10f, 10f));

        interpolator.Interpolate(25);

        Assert.Equal(10f, interpolator.Current.Pos.X, 4);
        Assert.Equal(5f, interpolator.Current.Pos.Y, 4);
    }

    /// <summary>
    /// Lo importante de la fase: <b>no se extrapola</b>. Si el buffer se seca, la entidad mantiene
    /// la última pose conocida en vez de seguir andando hacia donde nadie ha dicho que fuera.
    /// </summary>
    [Fact]
    public void PorDelanteDeLaUltimaMuestraMantieneLaPoseYNoExtrapola()
    {
        var interpolator = new EntityInterpolator(At(0f, 0f));
        interpolator.PushSample(10, At(0f, 0f));
        interpolator.PushSample(20, At(10f, 0f));

        interpolator.Interpolate(200);

        Assert.Equal(new Vec2(10f, 0f), interpolator.Current.Pos);
    }

    [Fact]
    public void PorDetrasDeLaPrimeraMuestraSeQuedaEnElla()
    {
        var interpolator = new EntityInterpolator(At(9f, 9f));
        interpolator.PushSample(10, At(0f, 0f));
        interpolator.PushSample(20, At(10f, 0f));

        interpolator.Interpolate(5);

        Assert.Equal(new Vec2(0f, 0f), interpolator.Current.Pos);
    }

    /// <summary>Orientación y animación son estados discretos: se toman de la muestra de destino.</summary>
    [Fact]
    public void LaOrientacionNoSeInterpola()
    {
        var interpolator = new EntityInterpolator(At(0f, 0f));
        interpolator.PushSample(10, At(0f, 0f, Facing.North, AnimState.Idle));
        interpolator.PushSample(20, At(10f, 0f, Facing.East, AnimState.Walk));

        interpolator.Interpolate(11);

        Assert.Equal(Facing.East, interpolator.Current.Facing);
        Assert.Equal(AnimState.Walk, interpolator.Current.Anim);
    }

    [Fact]
    public void ElBufferNoCreceSinLimite()
    {
        var interpolator = new EntityInterpolator(At(0f, 0f));

        for (var tick = 0; tick < EntityInterpolator.Capacity * 3; tick++)
        {
            interpolator.PushSample(tick, At(tick, 0f));
        }

        Assert.Equal(EntityInterpolator.Capacity, interpolator.SampleCount);
    }

    /// <summary>
    /// Podar deja siempre el tramo que se está usando: si se quedara sin el extremo izquierdo, la
    /// entidad saltaría al siguiente punto en vez de deslizarse hasta él.
    /// </summary>
    [Fact]
    public void PodarConservaElTramoQueSeEstaInterpolando()
    {
        var interpolator = new EntityInterpolator(At(0f, 0f));
        interpolator.PushSample(10, At(0f, 0f));
        interpolator.PushSample(20, At(10f, 0f));
        interpolator.PushSample(30, At(20f, 0f));

        interpolator.TrimBefore(25);
        interpolator.Interpolate(25);

        Assert.Equal(15f, interpolator.Current.Pos.X, 4);
        Assert.True(interpolator.SampleCount >= 2);
    }
}
