namespace Epimeteo.Server.Shop;

/// <summary>
/// Stock actual de un ítem de una tienda, en memoria — autoritativo mientras dura el proceso,
/// igual que un <c>ItemStack</c> (FASE-07 §2 D1). Un solo hilo (el del tick) lo toca.
/// </summary>
public sealed class ShopStockState
{
    public required string DefKey { get; init; }

    /// <summary><c>null</c> = stock infinito: nunca decrece ni se repone.</summary>
    public int? Stock { get; set; }

    /// <summary><c>null</c> = usar el precio de <c>content/shops/*.json</c> (mismo contrato que <c>docs/02</c>).</summary>
    public long? PriceBuyOverride { get; set; }

    public long? PriceSellOverride { get; set; }

    /// <summary>
    /// Próxima reposición, en <see cref="DateTimeOffset.UtcNow"/> — no <c>ServerClock</c>. El
    /// restock es un horario de mundo real (igual que el ciclo diario de granja de la Fase 8,
    /// <c>docs/00 §7</c>), no un cálculo de simulación: tiene que sobrevivir a un
    /// <c>systemctl restart</c> con el mismo sentido de "dentro de 6 horas reales", cosa que un
    /// reloj monotónico-desde-el-arranque no puede dar (se reinicia a cero en cada arranque).
    /// </summary>
    public DateTimeOffset RestockAt { get; set; }
}
