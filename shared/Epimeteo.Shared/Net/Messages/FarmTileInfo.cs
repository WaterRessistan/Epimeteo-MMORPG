using Epimeteo.Shared.Data;
using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Estado de un tile de granja tal como lo ve el cliente. Sin catálogo de cultivos en el cliente (FASE-08 §4): <see cref="Stage"/> ya viene resuelto por el servidor.</summary>
[MessagePackObject]
public sealed record FarmTileInfo
{
    [Key(0)]
    public required int TileX { get; init; }

    [Key(1)]
    public required int TileY { get; init; }

    [Key(2)]
    public required FarmTileStatus State { get; init; }

    /// <summary><c>null</c> si el tile no tiene nada plantado.</summary>
    [Key(3)]
    public string? CropKey { get; init; }

    /// <summary>Índice dentro de <c>CropDefinition.Stages</c>, cosmético. <c>0</c> si no hay cultivo.</summary>
    [Key(4)]
    public required byte Stage { get; init; }

    /// <summary>Si ya se regó en el día de granja actual.</summary>
    [Key(5)]
    public required bool Watered { get; init; }

    /// <summary>Estimación optimista hasta que esté listo, en ms. <c>0</c> si no aplica (FASE-08 §2 D12).</summary>
    [Key(6)]
    public required long MsRemaining { get; init; }
}
