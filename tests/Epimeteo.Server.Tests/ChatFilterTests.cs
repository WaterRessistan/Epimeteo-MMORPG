using Epimeteo.Server.Chat;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>Filtro básico de chat (FASE-11 §1): censura una lista fija, no un servicio de moderación.</summary>
public sealed class ChatFilterTests
{
    [Theory]
    [InlineData("eres un gilipollas", "eres un **********")]
    [InlineData("GILIPOLLAS en mayúsculas", "********** en mayúsculas")]
    [InlineData("menuda mierda de día", "menuda ****** de día")]
    public void CensuraLasPalabrasDeLaLista(string entrada, string esperado) =>
        Assert.Equal(esperado, ChatFilter.Censor(entrada));

    [Fact]
    public void NoTocaPalabrasQueSoloContienenUnaBloqueadaComoSubcadena() =>
        Assert.Equal("mierdal no es una palabra", ChatFilter.Censor("mierdal no es una palabra"));

    [Fact]
    public void TextoSinNadaBloqueadoSeQuedaIgual() =>
        Assert.Equal("hola, buenas tardes a todos", ChatFilter.Censor("hola, buenas tardes a todos"));
}
