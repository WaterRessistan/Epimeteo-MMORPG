using Epimeteo.Shared.Net;
using Epimeteo.Shared.Net.Messages;

namespace Epimeteo.Server.World;

/// <summary>
/// Arma el <c>Snapshot</c> de cada jugador a 10 Hz: las entidades de su área de interés que hayan
/// cambiado desde el snapshot anterior, más siempre la suya propia.
/// <para>
/// La propia va siempre porque lleva <c>LastAckedInputSeq</c>, y sin ese ack el cliente no puede
/// vaciar su buffer de predicción ni reconciliar. Las demás sólo si cambiaron: en un pueblo con
/// veinte personas quietas, un snapshot pesa casi nada.
/// </para>
/// </summary>
public sealed class SnapshotBuilder
{
    private readonly IReadOnlyDictionary<int, WorldEntity> _entities;
    private readonly List<EntityDelta> _deltas = [];

    public SnapshotBuilder(IReadOnlyDictionary<int, WorldEntity> entities) => _entities = entities;

    /// <summary>Manda el snapshot del tick a un jugador.</summary>
    public void Send(PlayerEntity player, long tick)
    {
        _deltas.Clear();
        _deltas.Add(player.ToDelta());

        foreach (var id in player.Known)
        {
            if (_entities.TryGetValue(id, out var entity) && entity.LastChangedTick > player.LastSnapshotTick)
            {
                _deltas.Add(entity.ToDelta());
            }
        }

        player.Peer.Send(Opcode.Snapshot, new S2CSnapshot
        {
            ServerTick = tick,
            LastAckedInputSeq = player.Inputs.LastAckedSeq,
            Entities = [.. _deltas],
        });

        player.LastSnapshotTick = tick;
    }
}
