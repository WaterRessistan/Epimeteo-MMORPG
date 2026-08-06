namespace Epimeteo.Server.Content;

/// <summary>
/// Estación de un cultivo. <c>Any</c> crece todo el año — el único cultivo de esta fase
/// (<c>crop.wheat</c>) la usa a propósito, para que la verificación E2E no dependa de en qué mes
/// real se ejecute (FASE-08 §2 D8). Servidor-only: el cliente no pinta estación todavía.
/// </summary>
public enum FarmSeason
{
    Any = 0,
    Spring = 1,
    Summer = 2,
    Autumn = 3,
    Winter = 4,
}
