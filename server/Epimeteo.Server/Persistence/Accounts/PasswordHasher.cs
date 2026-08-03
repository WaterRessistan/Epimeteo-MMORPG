using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Epimeteo.Server.Persistence.Accounts;

/// <summary>
/// Hash y verificación de contraseñas con Argon2id. El resultado de <see cref="Hash"/> es un
/// único string autocontenido (formato tipo PHC: <c>$argon2id$v=19$m=,t=,p=$sal$hash</c>) para
/// poder subir los parámetros de coste en el futuro sin invalidar los hashes ya guardados —
/// <see cref="Verify"/> lee los parámetros del propio string, nunca de una constante.
/// </summary>
public sealed class PasswordHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    // Parámetros de partida: mínimo recomendado por OWASP para Argon2id (19 MiB, 2 iteraciones,
    // paralelismo 1). Se suben si se mide que el hardware de producción lo aguanta sin más.
    private const int DefaultMemoryKb = 19 * 1024;
    private const int DefaultIterations = 2;
    private const int DefaultParallelism = 1;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = ComputeHash(password, salt, DefaultMemoryKb, DefaultIterations, DefaultParallelism, HashBytes);
        return Encode(DefaultMemoryKb, DefaultIterations, DefaultParallelism, salt, hash);
    }

    public bool Verify(string password, string encoded)
    {
        if (!TryDecode(encoded, out var memoryKb, out var iterations, out var parallelism, out var salt, out var expected))
        {
            return false;
        }

        var actual = ComputeHash(password, salt, memoryKb, iterations, parallelism, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] ComputeHash(
        string password, byte[] salt, int memoryKb, int iterations, int parallelism, int hashLength)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKb,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };

        return argon2.GetBytes(hashLength);
    }

    private static string Encode(int memoryKb, int iterations, int parallelism, byte[] salt, byte[] hash) =>
        $"$argon2id$v=19$m={memoryKb},t={iterations},p={parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";

    private static bool TryDecode(
        string encoded, out int memoryKb, out int iterations, out int parallelism, out byte[] salt, out byte[] hash)
    {
        memoryKb = 0;
        iterations = 0;
        parallelism = 0;
        salt = [];
        hash = [];

        var parts = encoded.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 || parts[0] != "argon2id")
        {
            return false;
        }

        var costParams = parts[2].Split(',')
            .Select(p => p.Split('=', 2))
            .Where(kv => kv.Length == 2)
            .ToDictionary(kv => kv[0], kv => kv[1]);

        if (!costParams.TryGetValue("m", out var mText) || !int.TryParse(mText, out memoryKb) ||
            !costParams.TryGetValue("t", out var tText) || !int.TryParse(tText, out iterations) ||
            !costParams.TryGetValue("p", out var pText) || !int.TryParse(pText, out parallelism))
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[3]);
            hash = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        return true;
    }
}
