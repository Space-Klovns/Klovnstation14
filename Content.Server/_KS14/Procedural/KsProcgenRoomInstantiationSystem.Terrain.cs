using System.Threading;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;

namespace Content.Server._KS14.Procedural;

internal sealed record KsProcgenRoomInstance(KsProcgenRoomTerrainPlan Terrain, IReadOnlyDictionary<int, EntityUid> Members,
    EntityUid FrameUid, Vector2i Origin, int QuarterTurns);

public sealed partial class KsProcgenRoomInstantiationSystem
{
    [Dependency] private KsProcgenRoomTerrainSystem _terrain = default!;

    /// <summary>Copy terrain and entities in one caller-owned transaction; failure requires retiring that transaction.</summary>
    internal bool TryInstantiateRoom(KsProcgenRoomMapManifest source, KsProcgenRoomEntityExtraction extraction,
        string roomId, EntityUid frameUid, Vector2i origin, int quarterTurns, List<EntityUid> allocations,
        int maximumAllocations, out KsProcgenRoomInstance? instance, out KsProcgenIssue? issue,
        int maximumTiles = 65_536, int maximumSourceTileProbes = 262_144, int maximumImageCharacters = 4_194_304,
        CancellationToken cancellationToken = default)
    {
        instance = null; issue = null;
        if (maximumImageCharacters is < 0 or > 67_108_864)
            return Reject("InvalidRoomTerrainInput", "Room copying requires a bounded image allowance.", out issue);
        if (!_terrain.TryPrepare(source, extraction, roomId, origin, quarterTurns, out var terrain, out issue,
                maximumTiles: maximumTiles, maximumSourceTileProbes: maximumSourceTileProbes, cancellationToken: cancellationToken)) return false;
        if (!_terrain.CheckContext(frameUid, allocations, maximumAllocations, out issue)) return false;
        if (!TryPrepare(extraction, roomId, origin, quarterTurns, maximumAllocations - allocations.Count,
                out var entities, out issue, maximumImageCharacters: maximumImageCharacters, cancellationToken: cancellationToken)) return false;
        if (!_terrain.TryWrite(terrain!, frameUid, allocations, maximumAllocations, out issue, cancellationToken: cancellationToken)) return false;
        if (!TryInstantiatePrepared(entities!, frameUid, allocations, maximumAllocations,
                out var members, out issue, cancellationToken: cancellationToken)) return false;
        try
        {
            if (!_terrain.Verify(terrain!, frameUid, allocations, maximumAllocations, out issue, cancellationToken: cancellationToken)) return false;
        }
        catch (OperationCanceledException)
        { return Reject("RoomTerrainCancelled", "Final terrain verification was cancelled; retire the owning transaction.", out issue); }
        catch (Exception exception)
        {
            Log.Debug($"Final room terrain verification failed: {exception}");
            return Reject("RoomTerrainEngineFailure", "Final terrain verification failed; retire the owning transaction.", out issue);
        }
        instance = new(terrain!, members!, frameUid, origin, quarterTurns); return true;
    }
}
