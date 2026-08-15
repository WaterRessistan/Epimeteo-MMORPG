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

        // Combate y habilidades (opcodes Attack/SkillCast, prefijo "combat." en SystemMessage —
        // FASE-09/10). Hasta esta sesión el cliente no traducía ninguno de estos: el mensaje
        // llegaba pero no se mostraba en ningún sitio que se viera durante el combate, así que un
        // ataque rechazado se sentía como "no funciona" en vez de decir por qué (bug real, no sólo
        // falta de arte).
        ResultCode.TargetNotFound => "Ese objetivo ya no está",
        ResultCode.TargetDead => "Ese objetivo ya está muerto",
        ResultCode.OnCooldown => "Todavía no puedes golpear otra vez",
        ResultCode.NotEnoughMana => "No tienes maná suficiente",
        ResultCode.OutOfRange => "Fuera de alcance",
        ResultCode.CannotAttackTarget => "No puedes atacar eso",
        ResultCode.SafeZone => "No se puede atacar en zona segura",
        ResultCode.TargetInSafeZone => "El objetivo está en zona segura",
        ResultCode.SkillNotUnlocked => "Todavía no tienes esa habilidad",
        ResultCode.InCombat => "Sigues en combate",
        ResultCode.LevelDifferenceTooHigh => "Diferencia de nivel demasiado alta",
        ResultCode.NoStatPointsAvailable => "No te quedan puntos por repartir",

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
