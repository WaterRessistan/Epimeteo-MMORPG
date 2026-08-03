using Epimeteo.Server.Persistence.Accounts;
using Xunit;

namespace Epimeteo.Server.Tests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Verify_ConLaContraseñaCorrecta_DevuelveTrue()
    {
        var encoded = _hasher.Hash("correcta-123");

        Assert.True(_hasher.Verify("correcta-123", encoded));
    }

    [Fact]
    public void Verify_ConOtraContraseña_DevuelveFalse()
    {
        var encoded = _hasher.Hash("correcta-123");

        Assert.False(_hasher.Verify("incorrecta-456", encoded));
    }

    [Fact]
    public void Hash_UsaSalAleatoria_DosHashesDeLaMismaContraseñaSonDistintos()
    {
        var first = _hasher.Hash("misma-contraseña");
        var second = _hasher.Hash("misma-contraseña");

        Assert.NotEqual(first, second);
        Assert.True(_hasher.Verify("misma-contraseña", first));
        Assert.True(_hasher.Verify("misma-contraseña", second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-es-el-formato-esperado")]
    [InlineData("$argon2id$v=19$m=abc,t=2,p=1$c2Fs$aGFzaA==")]
    public void Verify_ConUnEncodedIlegible_DevuelveFalseSinLanzar(string encoded)
    {
        Assert.False(_hasher.Verify("cualquiera", encoded));
    }
}
