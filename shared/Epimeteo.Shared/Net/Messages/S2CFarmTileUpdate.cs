using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>
/// Estado de una parcela entera: al entrar al mundo, tras cada acción y en el barrido diario. Se
/// manda a toda la zona del mapa de la parcela, sin filtrar por AOI (FASE-08 §2 D11).
/// </summary>
[MessagePackObject]
public sealed record S2CFarmTileUpdate
{
    [Key(0)]
    public required FarmTileInfo[] Tiles { get; init; }
}
