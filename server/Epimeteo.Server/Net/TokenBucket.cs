using Epimeteo.Shared.Time;

namespace Epimeteo.Server.Net;

/// <summary>
/// Token bucket sencillo con relleno continuo. No es thread-safe a propósito: cada instancia
/// pertenece a una sesión y sólo la toca su bucle de lectura.
/// </summary>
public sealed class TokenBucket
{
    private readonly double _ratePerMs;
    private readonly double _capacity;
    private double _tokens;
    private long _lastRefillMs;

    /// <param name="ratePerSecond">Mensajes por segundo sostenidos.</param>
    /// <param name="burst">Tokens acumulables como máximo (ráfaga permitida).</param>
    public TokenBucket(double ratePerSecond, double burst)
    {
        _ratePerMs = ratePerSecond / 1000.0;
        _capacity = burst;
        _tokens = burst;
        _lastRefillMs = ServerClock.NowMs;
    }

    /// <summary>Consume un token. Falso si el cubo está vacío (mensaje a descartar).</summary>
    public bool TryConsume(long nowMs)
    {
        var elapsed = nowMs - _lastRefillMs;
        if (elapsed > 0)
        {
            _lastRefillMs = nowMs;
            _tokens = Math.Min(_capacity, _tokens + (elapsed * _ratePerMs));
        }

        if (_tokens < 1.0)
        {
            return false;
        }

        _tokens -= 1.0;
        return true;
    }
}
