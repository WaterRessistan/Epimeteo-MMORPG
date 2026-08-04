using System.Net;
using Epimeteo.Shared.Net;

namespace Epimeteo.Server.Persistence.Accounts;

/// <summary>
/// Orquesta login y registro: valida entrada, aplica el rate limit persistido de
/// <c>docs/01-protocolo.md</c> (5/minuto por IP), verifica contraseña y emite el token de sesión.
/// El manejador de mensajes (hilo de red) sólo traduce <see cref="AuthOutcome"/> a
/// <see cref="Epimeteo.Shared.Net.Messages.S2CAuthResult"/> y a la transición de estado.
/// </summary>
public sealed class AuthService(
    AccountRepository accounts,
    LoginAttemptRepository loginAttempts,
    PasswordHasher passwordHasher,
    SessionTokenService sessionTokens,
    ServerOptions options)
{
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(1);

    // Verificado cuando la cuenta no existe, para que un login con usuario inexistente tarde lo
    // mismo que uno con contraseña incorrecta: si no, el tiempo de respuesta filtra qué usuarios
    // existen (Argon2id sólo se calcularía cuando FindByUsernameAsync sí encuentra la cuenta).
    private static readonly string DummyHash = new PasswordHasher().Hash("no-existe-ninguna-cuenta-con-este-usuario");

    public async Task<AuthOutcome> LoginAsync(
        string remoteAddress, string username, string password, CancellationToken ct = default)
    {
        var ip = ParseIp(remoteAddress);

        if (await loginAttempts.CountRecentAsync(ip, AttemptWindow, ct).ConfigureAwait(false) >= options.LoginAttemptsPerMinute)
        {
            // Ni siquiera se comprueba la contraseña: contarla como intento premiaría a quien
            // manda credenciales basura sólo para gastar el cupo de otro.
            return AuthOutcome.Fail(ResultCode.RateLimited);
        }

        if (!IsUsernameValid(username) || !IsPasswordValid(password))
        {
            await loginAttempts.RecordAsync(ip, username, success: false, ct).ConfigureAwait(false);
            return AuthOutcome.Fail(ResultCode.InvalidCredentials);
        }

        var account = await accounts.FindByUsernameAsync(username, ct).ConfigureAwait(false);
        var passwordMatches = passwordHasher.Verify(password, account?.PasswordHash ?? DummyHash);

        if (account is null || !passwordMatches)
        {
            await loginAttempts.RecordAsync(ip, username, success: false, ct).ConfigureAwait(false);
            return AuthOutcome.Fail(ResultCode.InvalidCredentials);
        }

        if (account.Status is AccountStatus.Banned or AccountStatus.Suspended or AccountStatus.Deleted)
        {
            await loginAttempts.RecordAsync(ip, username, success: false, ct).ConfigureAwait(false);
            return AuthOutcome.Fail(ResultCode.AccountBanned);
        }

        await loginAttempts.RecordAsync(ip, username, success: true, ct).ConfigureAwait(false);
        await accounts.TouchLastLoginAsync(account.Id, remoteAddress, ct).ConfigureAwait(false);
        var token = await sessionTokens.IssueAsync(account.Id, remoteAddress, ct).ConfigureAwait(false);

        return AuthOutcome.Success(account.Id, token);
    }

    public async Task<AuthOutcome> RegisterAsync(
        string remoteAddress, string username, string? email, string password, CancellationToken ct = default)
    {
        var ip = ParseIp(remoteAddress);

        if (await loginAttempts.CountRecentAsync(ip, AttemptWindow, ct).ConfigureAwait(false) >= options.LoginAttemptsPerMinute)
        {
            return AuthOutcome.Fail(ResultCode.RateLimited);
        }

        // A partir de aquí cada desenlace se registra en login_attempts: Register comparte el
        // mismo cubo persistido de 5/minuto por IP que Login (docs/01-protocolo.md), si no,
        // machacar el formulario de registro no gastaría nunca el cupo de nadie.
        if (!IsUsernameValid(username))
        {
            await loginAttempts.RecordAsync(ip, username, success: false, ct).ConfigureAwait(false);
            return AuthOutcome.Fail(ResultCode.NameInvalid);
        }

        if (!IsPasswordValid(password))
        {
            await loginAttempts.RecordAsync(ip, username, success: false, ct).ConfigureAwait(false);
            return AuthOutcome.Fail(ResultCode.PasswordInvalid);
        }

        if (!IsEmailValid(email))
        {
            // Sin código dedicado para email malformado en esta fase (docs/fases/FASE-02 §6):
            // añadir uno nuevo al enum compartido por un campo opcional no compensa.
            await loginAttempts.RecordAsync(ip, username, success: false, ct).ConfigureAwait(false);
            return AuthOutcome.Fail(ResultCode.NameInvalid);
        }

        var passwordHash = passwordHasher.Hash(password);
        var accountId = await accounts.CreateAsync(username, email, passwordHash, ct).ConfigureAwait(false);
        if (accountId is null)
        {
            await loginAttempts.RecordAsync(ip, username, success: false, ct).ConfigureAwait(false);
            return AuthOutcome.Fail(ResultCode.AccountAlreadyExists);
        }

        await loginAttempts.RecordAsync(ip, username, success: true, ct).ConfigureAwait(false);
        var token = await sessionTokens.IssueAsync(accountId.Value, remoteAddress, ct).ConfigureAwait(false);
        return AuthOutcome.Success(accountId.Value, token);
    }

    private static bool IsUsernameValid(string username) =>
        username.Length is >= 3 and <= 20 && username.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private static bool IsPasswordValid(string password) => password.Length is >= 8 and <= 72;

    private static bool IsEmailValid(string? email) =>
        email is null || (email.Length <= 254 && email.Contains('@') && !email.Any(char.IsControl));

    /// <summary>
    /// Dirección no parseable (p. ej. "desconocida" cuando <c>Connection.RemoteIpAddress</c>
    /// viene nulo) cae en <see cref="IPAddress.None"/> como cubo defensivo: no debería pasar en
    /// producción, y agrupar esos casos raros no es peor que dejarlos sin límite.
    /// </summary>
    private static IPAddress ParseIp(string remoteAddress) =>
        IPAddress.TryParse(remoteAddress, out var parsed) ? parsed : IPAddress.None;
}
