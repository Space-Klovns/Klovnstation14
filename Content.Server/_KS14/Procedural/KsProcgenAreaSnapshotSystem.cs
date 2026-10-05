using System.Linq;
using System.Numerics;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Map.Components;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server._KS14.Procedural;

/// <summary>Read-only paint snapshot. Does not start generation, strip markers or publish any blob.</summary>
public sealed partial class KsProcgenAreaSnapshotSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    public KsProcgenAreaNormalization Snapshot(EntityUid gridUid, string stableHostKey,
        IReadOnlySet<Vector2i>? keepVoid = null, int maximumMarkers = 65_536,
        int maximumBlobs = 4096, int maximumScannedMarkers = 131_072, int maximumEntrances = 1024)
    {
        KsProcgenAreaNormalization Fail(KsProcgenStatus status, string code) => new()
        {
            Status = status, Issue = new(code, "Grid area markers could not be snapshotted."),
        };
        if (maximumMarkers is < 0 or > 65_536 || maximumBlobs is < 0 or > 4096 ||
            maximumScannedMarkers is < 0 or > 131_072 || maximumEntrances is < 0 or > 1024 ||
            !TryComp<MapGridComponent>(gridUid, out var gridComponent) ||
            !TryComp<TransformComponent>(gridUid, out var gridTransformComponent) ||
            EntityManager.IsQueuedForDeletion(gridUid))
            return Fail(KsProcgenStatus.InvalidInput, "InvalidAreaSnapshotInput");
        if (gridComponent.TileSize != 1)
            return Fail(KsProcgenStatus.InvalidInput, "UnsupportedAreaGridTileSize");
        var markers = new List<KsProcgenAreaMarker>();
        var profiles = new Dictionary<string, KsProcgenAreaProfile>(StringComparer.Ordinal);
        var connectionProfiles = new Dictionary<string, IReadOnlyList<KsProcgenEntranceConfigurationSpec>>(StringComparer.Ordinal);
        var scanned = 0;
        // Authoring and private previews can be paused; ordinary system queries exclude them.
        var query = EntityManager.AllEntityQueryEnumerator<KsProcgenAreaCellComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var markerUid, out var markerComponent, out var transformComponent, out var metadataComponent))
        {
            if (scanned++ >= maximumScannedMarkers)
                return Fail(KsProcgenStatus.BudgetExceeded, "AreaSnapshotScanBudget");
            // Resolve the hierarchy's grid cache through the engine API before using it as scope.
            _transformSystem.GetMoverCoordinates(markerUid, transformComponent);
            if (transformComponent.ParentUid != gridUid && transformComponent.GridUid != gridUid)
                continue;
            if (EntityManager.IsQueuedForDeletion(markerUid) || metadataComponent.EntityLifeStage >= EntityLifeStage.Terminating)
                return Fail(KsProcgenStatus.InvalidInput, "AreaMarkerLifecycleInvalid");
            if (markers.Count >= maximumMarkers)
                return Fail(KsProcgenStatus.BudgetExceeded, "AreaMarkerBudget");
            if (!ReadCell(gridUid, gridTransformComponent, transformComponent, out var cell))
                return Fail(KsProcgenStatus.InvalidInput, "AreaMarkerNotTileAligned");
            if (!ProtoMan.TryIndex(markerComponent.Profile, out var profilePrototype) || profilePrototype.Limits == null ||
                profilePrototype.Theme != null && !ProtoMan.HasIndex<KsProcgenRoomThemePrototype>(profilePrototype.Theme))
                return Fail(KsProcgenStatus.InvalidInput, "InvalidAreaProfileReference");
            profiles.TryAdd(profilePrototype.ID, new(profilePrototype.ID, profilePrototype.Mode, profilePrototype.GeometryMode,
                profilePrototype.ConnectivityPolicy, profilePrototype.Theme,
                profilePrototype.Limits.MaxCells, profilePrototype.Limits.MaxAbsoluteCoordinate,
                EntranceConnections: profilePrototype.EntranceConnections));
            if (profilePrototype.EntranceConnections != null)
            {
                if (!ProtoMan.TryIndex<KsProcgenEntranceConnectionsPrototype>(profilePrototype.EntranceConnections, out var connectionPrototype) ||
                    connectionPrototype.Configurations == null)
                    return Fail(KsProcgenStatus.InvalidInput, "InvalidEntranceConnectionProfile");
                connectionProfiles.TryAdd(connectionPrototype.ID, connectionPrototype.Configurations);
            }
            markers.Add(new(cell, markerComponent.Channel,
                profilePrototype.ID, markerComponent.BlobId));
        }
        var entrances = new List<KsProcgenEntranceMarker>();
        var entranceQuery = EntityManager.AllEntityQueryEnumerator<KsProcgenEntranceComponent, TransformComponent, MetaDataComponent>();
        while (entranceQuery.MoveNext(out var markerUid, out var markerComponent, out var transformComponent, out var metadataComponent))
        {
            if (scanned++ >= maximumScannedMarkers)
                return Fail(KsProcgenStatus.BudgetExceeded, "AreaSnapshotScanBudget");
            _transformSystem.GetMoverCoordinates(markerUid, transformComponent);
            if (transformComponent.ParentUid != gridUid && transformComponent.GridUid != gridUid)
                continue;
            if (EntityManager.IsQueuedForDeletion(markerUid) || metadataComponent.EntityLifeStage >= EntityLifeStage.Terminating)
                return Fail(KsProcgenStatus.InvalidInput, "AreaMarkerLifecycleInvalid");
            if (entrances.Count >= maximumEntrances)
                return Fail(KsProcgenStatus.BudgetExceeded, "EntranceMarkerBudget");
            if (!ReadCell(gridUid, gridTransformComponent, transformComponent, out var cell))
                return Fail(KsProcgenStatus.InvalidInput, "EntranceMarkerNotTileAligned");
            if (markerComponent.ThresholdOffsets == null || markerComponent.ThresholdOffsets.Count is < 1 or > 64)
                return Fail(KsProcgenStatus.InvalidInput, "InvalidEntranceSpan");
            var thresholds = new List<Vector2i>();
            foreach (var offset in markerComponent.ThresholdOffsets)
            {
                var thresholdX = (long) cell.X + offset.X;
                var thresholdY = (long) cell.Y + offset.Y;
                if (Math.Abs(thresholdX) > 1_000_000 || Math.Abs(thresholdY) > 1_000_000)
                    return Fail(KsProcgenStatus.InvalidInput, "InvalidEntranceSpan");
                thresholds.Add(new((int) thresholdX, (int) thresholdY));
            }
            entrances.Add(new(markerComponent.PortId, markerComponent.Channel, thresholds, markerComponent.InwardNormal,
                BlobId: markerComponent.BlobId, OptionalSealable: markerComponent.OptionalSealable));
        }
        return KsProcgenAreaNormalizer.Normalize(stableHostKey, markers, profiles.Values.ToArray(),
            keepVoid: keepVoid, maximumMarkers: maximumMarkers, maximumBlobs: maximumBlobs,
            entrances: entrances, maximumEntrances: maximumEntrances, connectionProfiles: connectionProfiles);
    }

    /// <summary>Explicit author/import action, separate from read-only snapshotting. Never creates a marker or floor.</summary>
    public KsProcgenIssue? BindMarkerToGrid(EntityUid gridUid, EntityUid markerUid, Vector2i cell)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var gridComponent) || gridComponent.TileSize != 1 ||
            !TryComp<TransformComponent>(gridUid, out var gridTransformComponent) ||
            !TryComp<TransformComponent>(markerUid, out var markerTransformComponent) || markerTransformComponent.Anchored ||
            !TryComp<MetaDataComponent>(markerUid, out var metadataComponent) || metadataComponent.EntityLifeStage >= EntityLifeStage.Terminating ||
            EntityManager.IsQueuedForDeletion(gridUid) || EntityManager.IsQueuedForDeletion(markerUid) ||
            !(HasComp<KsProcgenAreaCellComponent>(markerUid) || HasComp<KsProcgenEntranceComponent>(markerUid)) ||
            Math.Abs((long) cell.X) > 1_000_000 || Math.Abs((long) cell.Y) > 1_000_000)
            return new("InvalidAreaMarkerBinding", "Only live unanchored procgen metadata can bind to an exact unit-grid cell.");
        var ancestorUid = gridUid;
        for (var depth = 0; ancestorUid.IsValid(); depth++)
        {
            if (depth >= 256 || ancestorUid == markerUid || !TryComp<TransformComponent>(ancestorUid, out var ancestorTransformComponent))
                return new("InvalidAreaMarkerBinding", "Binding would create a transform cycle or exceed the ancestor bound.");
            ancestorUid = ancestorTransformComponent.ParentUid;
        }
        _transformSystem.SetCoordinates(markerUid, new EntityCoordinates(gridUid, new Vector2((float) cell.X + 0.5f, (float) cell.Y + 0.5f)));
        return null;
    }

    private bool ReadCell(EntityUid gridUid, TransformComponent gridTransformComponent,
        TransformComponent markerTransformComponent, out Vector2i cell)
    {
        var position = markerTransformComponent.ParentUid == gridUid ? markerTransformComponent.LocalPosition :
            _transformSystem.ToCoordinates((gridUid, gridTransformComponent),
                _transformSystem.ToMapCoordinates(markerTransformComponent.Coordinates)).Position;
        var localCell = position - new Vector2(0.5f, 0.5f);
        cell = default;
        if (!float.IsFinite(localCell.X) || !float.IsFinite(localCell.Y) ||
            Math.Abs(localCell.X) > 1_000_000f || Math.Abs(localCell.Y) > 1_000_000f ||
            localCell.X != MathF.Truncate(localCell.X) || localCell.Y != MathF.Truncate(localCell.Y))
            return false;
        cell = new((int) localCell.X, (int) localCell.Y);
        return true;
    }
}
