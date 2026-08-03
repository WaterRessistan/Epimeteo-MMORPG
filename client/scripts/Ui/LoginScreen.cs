using Epimeteo.Client.Net;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Godot;

namespace Epimeteo.Client.Ui;

/// <summary>
/// Pantalla de login (Fase 2). Usa el <see cref="NetClient"/> autoload: la conexión abierta en
/// <see cref="ConnectScreen"/> sigue viva al cambiar de escena.
/// </summary>
public partial class LoginScreen : Control
{
    private NetClient _net = null!;
    private LineEdit _username = null!;
    private LineEdit _password = null!;
    private Label _messageLabel = null!;
    private Button _loginButton = null!;
    private Button _registerButton = null!;

    public override void _Ready()
    {
        _net = GetNode<NetClient>("/root/NetClient");
        _username = GetNode<LineEdit>("Layout/Username");
        _password = GetNode<LineEdit>("Layout/Password");
        _messageLabel = GetNode<Label>("Layout/Message");
        _loginButton = GetNode<Button>("Layout/Buttons/Login");
        _registerButton = GetNode<Button>("Layout/Buttons/GoRegister");

        _loginButton.Pressed += OnLoginPressed;
        _registerButton.Pressed += OnRegisterPressed;
        _net.AuthResultReceived += OnAuthResult;
        _net.Kicked += OnKicked;
    }

    public override void _ExitTree()
    {
        _loginButton.Pressed -= OnLoginPressed;
        _registerButton.Pressed -= OnRegisterPressed;
        _net.AuthResultReceived -= OnAuthResult;
        _net.Kicked -= OnKicked;
    }

    private void OnLoginPressed()
    {
        _messageLabel.Text = string.Empty;
        _loginButton.Disabled = true;
        _net.Login(_username.Text, _password.Text);
    }

    private void OnRegisterPressed() => GetTree().ChangeSceneToFile("res://scenes/Register.tscn");

    private void OnAuthResult(S2CAuthResult result)
    {
        _loginButton.Disabled = false;

        if (result.Ok)
        {
            GetTree().ChangeSceneToFile("res://scenes/CharacterSelect.tscn");
            return;
        }

        _messageLabel.Modulate = Colors.Salmon;
        _messageLabel.Text = ResultCodeText.Describe(result.Code);
    }

    private void OnKicked(KickReason reason, ResultCode detail, int serverProtocolVersion)
    {
        GD.Print(ResultCodeText.Describe(reason, detail, serverProtocolVersion));
        GetTree().ChangeSceneToFile("res://scenes/Connect.tscn");
    }
}
