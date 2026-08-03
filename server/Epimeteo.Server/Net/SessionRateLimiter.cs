using Epimeteo.Shared.Net;

namespace Epimeteo.Server.Net;

/// <summary>
/// Rate limit por sesión y por familia de opcode (<c>docs/01-protocolo.md § Rate limiting</c>).
/// Pasarse descarta el mensaje y suma un strike; 3 strikes en 10 s cierran la sesión.
/// Las familias que aún no existen ya tienen aquí su límite: cuando su fase las implemente,
/// el límite ya está puesto y no se olvida.
/// </summary>
public sealed class SessionRateLimiter
{
    private const int StrikeWindowMs = 10_000;
    private const int MaxStrikes = 3;

    private readonly TokenBucket[] _buckets;
    private int _strikes;
    private long _firstStrikeMs;

    public SessionRateLimiter()
    {
        _buckets = new TokenBucket[Enum.GetValues<OpcodeFamily>().Length];
        _buckets[(int)OpcodeFamily.Session] = new TokenBucket(10, 20);
        _buckets[(int)OpcodeFamily.Auth] = new TokenBucket(1, 5);
        _buckets[(int)OpcodeFamily.Character] = new TokenBucket(5, 10);
        _buckets[(int)OpcodeFamily.Movement] = new TokenBucket(40, 60);
        _buckets[(int)OpcodeFamily.Inventory] = new TokenBucket(20, 40);
        _buckets[(int)OpcodeFamily.Shop] = new TokenBucket(10, 20);
        _buckets[(int)OpcodeFamily.Farm] = new TokenBucket(20, 40);
        _buckets[(int)OpcodeFamily.Combat] = new TokenBucket(20, 40);
        _buckets[(int)OpcodeFamily.Chat] = new TokenBucket(2, 5);
        _buckets[(int)OpcodeFamily.ServerOnly] = new TokenBucket(0, 0);
    }

    /// <summary>Strikes acumulados en la ventana actual.</summary>
    public int Strikes => _strikes;

    /// <summary>
    /// Verdadero si el mensaje pasa el límite. Falso si hay que descartarlo;
    /// en ese caso <paramref name="disconnect"/> indica si además toca cerrar la sesión.
    /// </summary>
    public bool TryAcquire(OpcodeFamily family, long nowMs, out bool disconnect)
    {
        if (_buckets[(int)family].TryConsume(nowMs))
        {
            disconnect = false;
            return true;
        }

        if (_strikes == 0 || nowMs - _firstStrikeMs > StrikeWindowMs)
        {
            _firstStrikeMs = nowMs;
            _strikes = 1;
        }
        else
        {
            _strikes++;
        }

        disconnect = _strikes >= MaxStrikes;
        return false;
    }
}
