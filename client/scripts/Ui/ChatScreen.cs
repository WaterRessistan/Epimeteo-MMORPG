using System.Collections.Generic;
using Epimeteo.Client.Net;
using Epimeteo.Client.World;
using Epimeteo.Shared.Data;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Godot;

namespace Epimeteo.Client.Ui;

/// <summary>
/// Chat de la Fase 11: un log de las últimas líneas y una caja de texto que se abre con
/// <c>Enter</c>. Sin arte, mismo criterio que el resto de <c>Ui/</c> — lo que importa es que el
/// mensaje llegue y se vea, no cómo se pinte.
/// <para>
/// El servidor decide si el texto es un mensaje normal o un comando de barra (<c>/w</c>, <c>/who</c>,
/// <c>/kick</c>...); esta pantalla no sabe qué comandos existen, sólo manda lo que se escribió al
/// canal elegido (FASE-11 §2 D1/D3) — <c>T</c> alterna entre global y zona antes de mandar.
/// </para>
/// </summary>
public partial class ChatScreen : CanvasLayer
{
    private const int MaxLines = 12;

    private readonly List<string> _lines = [];

    private NetClient _net = null!;
    private Label _log = null!;
    private LineEdit _input = null!;
    private ChatChannel _channel = ChatChannel.Global;

    /// <inheritdoc />
    public override void _Ready()
    {
        _net = GetNode<NetClient>("/root/NetClient");

        _log = new Label
        {
            AnchorLeft = 0f, AnchorRight = 0.4f, AnchorTop = 1f, AnchorBottom = 1f,
            OffsetLeft = 4, OffsetTop = -160, OffsetRight = -4, OffsetBottom = -28,
            VerticalAlignment = VerticalAlignment.Bottom,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _log.AddThemeFontSizeOverride("font_size", 8);
        AddChild(_log);

        _input = new LineEdit
        {
            Visible = false,
            MaxLength = ChatConstants.MaxMessageLength,
            AnchorLeft = 0f, AnchorRight = 0.4f, AnchorTop = 1f, AnchorBottom = 1f,
            OffsetLeft = 4, OffsetTop = -24, OffsetRight = -4, OffsetBottom = -4,
        };
        AddChild(_input);
        _input.TextSubmitted += OnSubmitted;
        UpdatePlaceholder();

        _net.ChatMessageReceived += OnChatMessage;
        _net.SystemMessageReceived += OnSystemMessage;
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _net.ChatMessageReceived -= OnChatMessage;
        _net.SystemMessageReceived -= OnSystemMessage;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_input.Visible)
        {
            return;
        }

        if (Input.IsActionJustPressed(InputActions.ChatFocus))
        {
            _input.Visible = true;
            _input.GrabFocus();
        }
        else if (Input.IsActionJustPressed(InputActions.ChatChannelToggle))
        {
            _channel = _channel == ChatChannel.Global ? ChatChannel.Zone : ChatChannel.Global;
            UpdatePlaceholder();
        }
    }

    private void UpdatePlaceholder() =>
        _input.PlaceholderText = _channel == ChatChannel.Global ? "[Global] Enter para mandar" : "[Zona] Enter para mandar";

    private void OnSubmitted(string text)
    {
        _input.Visible = false;
        _input.Text = string.Empty;

        if (!string.IsNullOrWhiteSpace(text))
        {
            _net.SendChatSend(_channel, text);
        }
    }

    private void OnChatMessage(S2CChatMessage message)
    {
        var tag = message.Channel switch
        {
            ChatChannel.Global => "[Global]",
            ChatChannel.Zone => "[Zona]",
            ChatChannel.Whisper => "[Susurro]",
            _ => "[Sistema]",
        };

        AddLine($"{tag} {message.SenderName}: {message.Text}");
    }

    /// <summary>
    /// Sólo las claves <c>chat.*</c>: el resto de pantallas ya atienden las suyas
    /// (<c>combat.</c>, <c>progression.</c>...) y esta no tiene por qué repetirlas.
    /// </summary>
    private void OnSystemMessage(S2CSystemMessage message)
    {
        if (!message.Key.StartsWith("chat.", System.StringComparison.Ordinal))
        {
            return;
        }

        var line = message.Key switch
        {
            "chat.who" => $"Conectados: {string.Join(", ", message.Args)}",
            "chat.help" => $"Comandos: {string.Join("  ", message.Args)}",
            "chat.AdminActionDone" => $"Hecho ({string.Join(", ", message.Args)})",
            _ => $"({message.Key["chat.".Length..]})",
        };

        AddLine(line);
    }

    private void AddLine(string line)
    {
        _lines.Add(line);
        if (_lines.Count > MaxLines)
        {
            _lines.RemoveAt(0);
        }

        _log.Text = string.Join('\n', _lines);
    }
}
