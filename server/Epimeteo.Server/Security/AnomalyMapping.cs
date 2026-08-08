using Epimeteo.Shared.Net;

namespace Epimeteo.Server.Security;

/// <summary>
/// Traduce el <see cref="ResultCode"/> de un rechazo a la anomalía que le corresponde, o
/// <c>null</c> si ese rechazo es juego normal y no debe contar (FASE-13 §2 D4).
/// <para>
/// Existe para no tener que tocar los ~29 puntos de <c>GameWorld</c> que rechazan algo: los
/// cuatro <c>Send*Failure</c> por los que pasan todos consultan esto una vez. Añadir un rechazo
/// nuevo en una fase futura queda cubierto solo.
/// </para>
/// </summary>
public static class AnomalyMapping
{
    /// <summary>
    /// La clave de esta tabla es distinguir <b>"no puedes"</b> de <b>"eso no debería haber
    /// llegado"</b>. Quedarse sin maná, atacar en zona segura o tener el inventario lleno son
    /// respuestas normales que un cliente honesto provoca constantemente — contarlas como
    /// anomalías sería echar a jugadores por jugar. Lo que sí cuenta es insistir en algo que la
    /// propia UI del cliente ya sabe que no vale.
    /// </summary>
    public static AnomalyKind? For(ResultCode code) => code switch
    {
        // Alcance y objetivo. El cliente conoce las posiciones y el alcance: pedir esto es, o
        // latencia (unas pocas veces), o un cliente que no mira (cientos).
        ResultCode.TooFarAway or ResultCode.OutOfRange => AnomalyKind.OutOfRange,
        ResultCode.TargetNotFound or ResultCode.TargetDead => AnomalyKind.OutOfRange,
        ResultCode.CannotAttackTarget => AnomalyKind.OutOfRange,

        // Dinero de por medio: un bucle probando precios es exactamente lo que interesa ver en el
        // log aunque el servidor nunca lo deje colar.
        ResultCode.PriceChanged or ResultCode.NotEnoughGold or ResultCode.OutOfStock => AnomalyKind.EconomyRejected,

        // Ítems que no se tienen o que no son lo que se dice: el cliente tiene el mismo
        // ItemCatalog y su propio espejo del inventario, así que esto no lo produce jugando.
        ResultCode.ItemNotFound or ResultCode.NotEnoughItems => AnomalyKind.EconomyRejected,

        // Todo lo demás —SafeZone, OnCooldown, NotEnoughMana, InventoryFull, WrongTool,
        // NotAuthorized...— es juego normal o UX, y no cuenta.
        _ => null,
    };
}
