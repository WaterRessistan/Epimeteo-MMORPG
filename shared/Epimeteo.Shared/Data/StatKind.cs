namespace Epimeteo.Shared.Data;

/// <summary>Qué stat base recibe el punto de <c>AllocateStatPoint</c> (FASE-10 §2 D4).</summary>
public enum StatKind : byte
{
    Str = 0,
    Int = 1,
    Vit = 2,
    Dex = 3,
}
