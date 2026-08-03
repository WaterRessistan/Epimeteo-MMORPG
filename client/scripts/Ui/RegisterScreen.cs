using Epimeteo.Client.Net;
using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;
using Godot;

namespace Epimeteo.Client.Ui;

/// <summary>Pantalla de registro (Fase 2). Comparte el <see cref="NetClient"/> autoload con <see cref="LoginScreen"/>.</summary>
public partial class RegisterScreen : Control
{
    private NetClient _net = null!;
    private LineEdit _username = null!;
    private LineEdit _email = null!;
    private LineEdit _password = null!;
    private LineEdit _confirmPassword = null!;
    private Label _messageLabel = null!;
    private Button _registerButton = null!;
    private Button _loginButton = null!;

    public override void _Ready()
    {
        _net = GetNode<NetClient>("/root/NetClient");
        _username = GetNode<LineEdit>("Layout/Username");
        _email = GetNode<LineEdit>("Layout/Email");
        _password = GetNode<LineEdit>("Layout/Password");
        _confirmPassword = GetNode<LineEdit>("Layout/ConfirmPassword");
        _messageLabel = GetNode<Label>("Layout/Message");
        _registerButton = GetNode<Button>("Layout/Buttons/Register");
        _loginButton = GetNode<Button>("Layout/Buttons/GoLogin");

        _registerButton.Pressed += OnRegisterPressed;
        _loginButton.Pressed += OnLoginPressed;
        _net.AuthResultReceived += OnAuthResult;
        _net.Kicked += OnKicked;
    }

    public override void _ExitTree()
    {
        _registerButton.Pressed -= OnRegisterPressed;
        _loginButton.Pressed -= OnLoginPressed;
        _net.AuthResultReceived -= OnAuthResult;
        _net.Kicked -= OnKicked;
    }

    private void OnRegisterPressed()
    {
        // Sólo UX: la validación real de verdad la hace el servidor (CLAUDE.md § seguridad).
        if (_password.Text != _confirmPassword.Text)
        {
            _messageLabel.Modulate = Colors.Salmon;
            _messageLabel.Text = "Las contraseñas no coinciden";
            return;
        }

        _messageLabel.Text = string.Empty;
        _registerButton.Disabled = true;
        var email = string.IsNullOrWhiteSpace(_email.Text) ? null : _email.Text;
        _net.Register(_username.Text, email, _password.Text);
    }

    private void OnLoginPressed() => GetTree().ChangeSceneToFile("res://scenes/Login.tscn");

    private void OnAuthResult(S2CAuthResult result)
    {
        _registerButton.Disabled = false;

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
