using Epimeteo.Server.Chat;
using Epimeteo.Shared.Net;
using Xunit;

namespace Epimeteo.Server.Tests;

/// <summary>Reconocimiento de comandos de barra (FASE-11 §2 D3), sin mundo ni red de por medio.</summary>
public sealed class ChatCommandParserTests
{
    [Fact]
    public void TextoSinBarraEsUnMensajeNormalAlCanalElegido()
    {
        var command = ChatCommandParser.Parse(ChatChannel.Zone, "hola a todos");
        var say = Assert.IsType<ChatCommand.Say>(command);
        Assert.Equal(ChatChannel.Zone, say.Channel);
        Assert.Equal("hola a todos", say.Text);
    }

    [Fact]
    public void SusurroConNombreYMensaje()
    {
        var command = ChatCommandParser.Parse(ChatChannel.Global, "/w Bob hola, ¿cómo estás?");
        var whisper = Assert.IsType<ChatCommand.Whisper>(command);
        Assert.Equal("Bob", whisper.TargetName);
        Assert.Equal("hola, ¿cómo estás?", whisper.Text);
    }

    [Fact]
    public void SusurroSinMensajeEsInvalido()
    {
        var command = ChatCommandParser.Parse(ChatChannel.Global, "/w Bob");
        var invalid = Assert.IsType<ChatCommand.Invalid>(command);
        Assert.Equal(ResultCode.InvalidCommand, invalid.Code);
    }

    [Fact]
    public void WhoYHelpNoNecesitanArgumentos()
    {
        Assert.IsType<ChatCommand.Who>(ChatCommandParser.Parse(ChatChannel.Global, "/who"));
        Assert.IsType<ChatCommand.Help>(ChatCommandParser.Parse(ChatChannel.Global, "/help"));
    }

    [Fact]
    public void KickConMotivo()
    {
        var command = ChatCommandParser.Parse(ChatChannel.Global, "/kick Bob spamea el canal global");
        var kick = Assert.IsType<ChatCommand.Kick>(command);
        Assert.Equal("Bob", kick.TargetName);
        Assert.Equal("spamea el canal global", kick.Reason);
    }

    [Fact]
    public void KickSinMotivoDejaElMotivoVacio()
    {
        var command = ChatCommandParser.Parse(ChatChannel.Global, "/kick Bob");
        var kick = Assert.IsType<ChatCommand.Kick>(command);
        Assert.Equal("Bob", kick.TargetName);
        Assert.Equal(string.Empty, kick.Reason);
    }

    [Fact]
    public void KickSinObjetivoEsInvalido() =>
        Assert.IsType<ChatCommand.Invalid>(ChatCommandParser.Parse(ChatChannel.Global, "/kick"));

    [Fact]
    public void BanConHorasYMotivo()
    {
        var command = ChatCommandParser.Parse(ChatChannel.Global, "/ban Bob 24 se porta mal");
        var ban = Assert.IsType<ChatCommand.Ban>(command);
        Assert.Equal("Bob", ban.TargetName);
        Assert.Equal(24, ban.Hours);
        Assert.Equal("se porta mal", ban.Reason);
    }

    [Fact]
    public void BanSinHorasEsInvalido() =>
        Assert.IsType<ChatCommand.Invalid>(ChatCommandParser.Parse(ChatChannel.Global, "/ban Bob"));

    [Fact]
    public void BanConHorasNoNumericasEsInvalido() =>
        Assert.IsType<ChatCommand.Invalid>(ChatCommandParser.Parse(ChatChannel.Global, "/ban Bob para-siempre"));

    [Fact]
    public void TeleportConUnSoloNombre()
    {
        var command = ChatCommandParser.Parse(ChatChannel.Global, "/teleport Bob");
        var teleport = Assert.IsType<ChatCommand.Teleport>(command);
        Assert.Equal("Bob", teleport.TargetName);
    }

    [Fact]
    public void TpEsUnAliasDeTeleport() =>
        Assert.IsType<ChatCommand.Teleport>(ChatCommandParser.Parse(ChatChannel.Global, "/tp Bob"));

    [Fact]
    public void GiveConDefKeyYCantidad()
    {
        var command = ChatCommandParser.Parse(ChatChannel.Global, "/give Bob item.iron_sword 3");
        var give = Assert.IsType<ChatCommand.Give>(command);
        Assert.Equal("Bob", give.TargetName);
        Assert.Equal("item.iron_sword", give.DefKey);
        Assert.Equal(3, give.Quantity);
    }

    [Fact]
    public void GiveConCantidadNoPositivaEsInvalido() =>
        Assert.IsType<ChatCommand.Invalid>(ChatCommandParser.Parse(ChatChannel.Global, "/give Bob item.iron_sword 0"));

    [Fact]
    public void HealConNombre()
    {
        var command = ChatCommandParser.Parse(ChatChannel.Global, "/heal Bob");
        var heal = Assert.IsType<ChatCommand.Heal>(command);
        Assert.Equal("Bob", heal.TargetName);
    }

    [Fact]
    public void HealSinNombreEsInvalido() =>
        Assert.IsType<ChatCommand.Invalid>(ChatCommandParser.Parse(ChatChannel.Global, "/heal"));

    [Fact]
    public void XpConNombreYCantidad()
    {
        var command = ChatCommandParser.Parse(ChatChannel.Global, "/xp Bob 5000");
        var grantXp = Assert.IsType<ChatCommand.GrantXp>(command);
        Assert.Equal("Bob", grantXp.TargetName);
        Assert.Equal(5000, grantXp.Amount);
    }

    [Fact]
    public void XpConCantidadNoPositivaEsInvalido() =>
        Assert.IsType<ChatCommand.Invalid>(ChatCommandParser.Parse(ChatChannel.Global, "/xp Bob 0"));

    [Fact]
    public void XpConCantidadNoNumericaEsInvalido() =>
        Assert.IsType<ChatCommand.Invalid>(ChatCommandParser.Parse(ChatChannel.Global, "/xp Bob muchisima"));

    [Fact]
    public void ComandoDesconocidoEsInvalido()
    {
        var command = ChatCommandParser.Parse(ChatChannel.Global, "/nosetoloqueesesto");
        var invalid = Assert.IsType<ChatCommand.Invalid>(command);
        Assert.Equal(ResultCode.InvalidCommand, invalid.Code);
    }

    [Fact]
    public void TextoVacioEsInvalido() =>
        Assert.IsType<ChatCommand.Invalid>(ChatCommandParser.Parse(ChatChannel.Global, "   "));
}
