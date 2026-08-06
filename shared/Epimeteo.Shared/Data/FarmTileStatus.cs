namespace Epimeteo.Shared.Data;

/// <summary>
/// Estado de un tile de granja, igual que <c>farm_tiles.state</c> (<c>docs/02 § Granja y
/// cultivos</c>). Viaja en <see cref="Net.Messages.FarmTileInfo"/>, así que vive en
/// <c>Shared</c>, no junto a <c>CropDefinition</c> (servidor-only, FASE-08 §4).
/// </summary>
public enum FarmTileStatus : byte
{
    Untilled = 0,
    Tilled = 1,
    Planted = 2,
    Ready = 3,
}
