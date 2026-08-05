using MessagePack;

namespace Epimeteo.Shared.Net.Messages;

/// <summary>Cerrar la tienda abierta. Sin payload.</summary>
[MessagePackObject]
public sealed record C2SShopClose;
