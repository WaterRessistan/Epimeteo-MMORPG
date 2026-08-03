using Epimeteo.Shared.Net;

namespace Epimeteo.Client.Ui;

/// <summary>
/// Traduce <see cref="ResultCode"/> y <see cref="KickReason"/> a texto en español. El servidor
/// nunca manda texto de error (docs/01-protocolo.md § Códigos de error); esta es la única tabla
/// de traducción del cliente, compartida por todas las pantallas.
/// </summary>
public static class ResultCodeText
{
    public static string Describe(ResultCode code) => code switch
    {
        ResultCode.Ok => string.Empty,
        ResultCode.RateLimited => "Demasiados intentos: espera un minuto",
        ResultCode.InvalidCredentials => "Usuario o contraseña incorrectos",
        ResultCode.AccountBanned => "Cuenta suspendida",
        ResultCode.AccountAlreadyExists => "Ese usuario ya existe",
        ResultCode.NameTaken => "Ese nombre ya está en uso",
        ResultCode.NameInvalid => "Usuario o email no válidos",
        ResultCode.PasswordInvalid => "La contraseña debe tener entre 8 y 72 caracteres",
        ResultCode.SlotOccupied => "Ese hueco ya tiene un personaje",
        ResultCode.NoCharacterSlots => "No hay huecos de personaje disponibles",
        ResultCode.CharacterNotFound => "Ese personaje no existe",
        _ => $"Error inesperado ({code})",
    };

    public static string Describe(KickReason reason, ResultCode detail, int serverProtocolVersion) => reason switch
    {
        KickReason.VersionMismatch =>
            $"Actualiza el juego: el servidor usa el protocolo v{serverProtocolVersion}, tú la v{ProtocolVersion.Current}",
        KickReason.RateLimited => "Demasiados mensajes enviados",
        KickReason.Timeout => "Sin respuesta del cliente",
        KickReason.ServerShutdown => "El servidor se está apagando",
        KickReason.Banned => "Cuenta expulsada",
        KickReason.LoggedInElsewhere => "Sesión abierta desde otro sitio",
        KickReason.InvalidState or KickReason.ProtocolError => $"Error de protocolo ({detail})",
        _ => $"Desconectado por el servidor ({reason})",
    };
}
