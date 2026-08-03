using System.Text.Json;

namespace Epimeteo.Server.Persistence.Characters;

/// <summary>Proyección de una fila de <c>characters</c> (personajes vivos, <c>deleted_at IS NULL</c>).</summary>
public sealed record Character
{
    public required long Id { get; init; }
    public required long AccountId { get; init; }
    public required int Slot { get; init; }
    public required string Name { get; init; }
    public required string ClassKey { get; init; }
    public required int Level { get; init; }
    public required long Xp { get; init; }
    public required int StatStr { get; init; }
    public required int StatInt { get; init; }
    public required int StatVit { get; init; }
    public required int StatDex { get; init; }
    public required int StatPoints { get; init; }
    public required int Hp { get; init; }
    public required int Mp { get; init; }
    public required long Gold { get; init; }
    public required string MapKey { get; init; }
    public required float PosX { get; init; }
    public required float PosY { get; init; }
    public required int Facing { get; init; }

    /// <summary>Columna <c>appearance jsonb</c> tal cual, sin parsear. Ver <see cref="PaletteIndex"/>.</summary>
    public required string AppearanceJson { get; init; }

    /// <summary>
    /// La única "apariencia" hasta que haya assets reales (FASE-03-personajes.md §4): 0 si el
    /// JSON no trae <c>palette</c> o está corrupto — no vale la pena una excepción por esto.
    /// </summary>
    public byte PaletteIndex
    {
        get
        {
            try
            {
                using var doc = JsonDocument.Parse(AppearanceJson);
                return doc.RootElement.TryGetProperty("palette", out var palette) ? (byte)palette.GetInt32() : (byte)0;
            }
            catch (JsonException)
            {
                return 0;
            }
        }
    }
}
