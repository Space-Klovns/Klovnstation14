using System.Numerics;
using Content.Client._KS14.Decals;
using Content.Shared.Decals;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client.Decals;

/// <summary>
///     Kept predicted decals (<see cref="KsKeptPredictedDecalsComponent"/>): the decals of a prediction that runs once,
///         shown until the server's arrive.
/// </summary>
public sealed partial class DecalSystem
{
    [Dependency] private IGameTiming _ksGameTiming = default!;
    [Dependency] private EntityQuery<KsKeptPredictedDecalsComponent> _ksKeptDecalsQuery = default!;

    /// <summary>
    ///     How long a kept decal waits for the server's.
    /// </summary>
    private static readonly TimeSpan KsKeptDecalLifetime = TimeSpan.FromSeconds(1.5);

    /// <summary>
    ///     How far the server's decal may be from the kept one it takes the place of. Both are laid out by the same rolls
    ///         from where each side had the thing that was hit, which is not quite the same place.
    /// </summary>
    private const float KsKeptDecalSupersedeDistance = 1f;

    public override void KsAddKeptPredictedDecal(string id, EntityCoordinates coordinates, Color? color = null, Angle? rotation = null, bool cleanable = false)
    {
        if (!ProtoMan.HasIndex<DecalPrototype>(id))
            return;

        var gridUid = _transform.GetGrid(coordinates);
        if (gridUid == null || !_gridQuery.TryComp(gridUid.Value, out var grid))
            return;

        if (_turf.IsSpace(_mapSystem.GetTileRef(gridUid.Value, grid, coordinates)))
            return;

        // In the grid's frame, as the grid's own decals are.
        var gridPosition = coordinates.EntityId == gridUid.Value
            ? coordinates.Position
            : Vector2.Transform(_transform.ToMapCoordinates(coordinates).Position, _transform.GetInvWorldMatrix(gridUid.Value));

        var decal = new Decal(gridPosition, id, color, rotation ?? Angle.Zero, 0, cleanable);
        EnsureComp<KsKeptPredictedDecalsComponent>(gridUid.Value).Decals.Add(new KsKeptPredictedDecal(decal, _ksGameTiming.CurTime + KsKeptDecalLifetime));
    }

    /// <summary>
    ///     The kept predicted decals of <paramref name="gridUid"/>, for what draws the grid's decals.
    /// </summary>
    public IReadOnlyList<KsKeptPredictedDecal> KsGetKeptDecals(EntityUid gridUid)
    {
        return _ksKeptDecalsQuery.TryComp(gridUid, out var keptComponent) ? keptComponent.Decals : Array.Empty<KsKeptPredictedDecal>();
    }

    /// <summary>
    ///     Each kept decal that one of the server's is the server's of (the same decal, in the same colour, close by) is
    ///         that one now, and goes.
    /// </summary>
    /// <remarks>
    ///     Looked for every tick, from <see cref="Update"/>, rather than when a chunk's state arrives: the first decals
    ///         on a chunk arrive with the chunk's entity, before it has a grid to be found by. A tick's systems run
    ///         after its states are applied and before anything is drawn, so the two are never drawn together.
    /// </remarks>
    private void KsSupersedeKeptDecals(EntityUid gridUid, KsKeptPredictedDecalsComponent keptComponent)
    {
        var keptDecals = keptComponent.Decals;
        for (var i = keptDecals.Count - 1; i >= 0; i--)
        {
            var keptDecal = keptDecals[i].Decal;
            var around = Box2.CenteredAround(keptDecal.Coordinates, new Vector2(KsKeptDecalSupersedeDistance * 2f));

            if (KsIsServed(gridUid, keptDecal, around))
                keptDecals.RemoveAt(i);
        }
    }

    private bool KsIsServed(EntityUid gridUid, Decal keptDecal, Box2 around)
    {
        foreach (var chunkEnt in ChunkEntities.GetChunksIntersecting(gridUid, around))
        {
            if (!DecalChunkQuery.TryComp(chunkEnt.Owner, out var chunkComponent))
                continue;

            foreach (var (decalId, serverDecal) in chunkComponent.Decals)
            {
                if (decalId >= DecalChunkComponent.MinPredictedDecalId)
                    continue; // this client's own prediction, not the server's

                if (serverDecal.Id == keptDecal.Id &&
                    serverDecal.Color == keptDecal.Color &&
                    Vector2.DistanceSquared(serverDecal.Coordinates, keptDecal.Coordinates) <= KsKeptDecalSupersedeDistance * KsKeptDecalSupersedeDistance)
                    return true;
            }
        }

        return false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _ksGameTiming.CurTime;
        var expire = _ksGameTiming.IsFirstTimePredicted;

        var query = EntityQueryEnumerator<KsKeptPredictedDecalsComponent>();
        while (query.MoveNext(out var gridUid, out var keptComponent))
        {
            if (keptComponent.LifeStage > ComponentLifeStage.Running)
                continue;

            KsSupersedeKeptDecals(gridUid, keptComponent);

            // And one the server never placed goes.
            if (expire)
                keptComponent.Decals.RemoveAll(kept => kept.ExpiresAt <= now);

            if (keptComponent.Decals.Count == 0)
                RemCompDeferred<KsKeptPredictedDecalsComponent>(gridUid);
        }
    }
}
