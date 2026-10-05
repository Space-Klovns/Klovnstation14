using System.Numerics;
using Content.Shared.Item;
using Content.Shared.Placeable;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;

namespace Content.Server._KS14.Procedural;

public enum KsProcgenSurfacePreflightStatus : byte
{
    Candidate,
    Rejected,
    InvalidInput,
    UnsupportedContent,
    BudgetExceeded,
}

/// <summary>Live drop/tracking prerequisites. No pose, contact, mounting or actor-access proof.</summary>
public sealed record KsProcgenSurfacePreflight(
    KsProcgenSurfacePreflightStatus Status, string Reason, bool HasTracker,
    uint? MaximumTrackedEntities, int TrackedEntities, bool Centered, Vector2 PositionOffset)
{
    public bool PlacementVerified => false;
}

/// <summary>
/// Read-only preflight for a future owned surface-drop adapter. ItemPlacer's whitelist/capacity
/// controls contact tracking, not generic PlaceableSurface drop permission. Neither is mounting.
/// </summary>
public sealed partial class KsProcgenSurfacePreflightSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;

    public KsProcgenSurfacePreflight Check(EntityUid subjectUid, EntityUid targetUid, string? slotId = null)
    {
        KsProcgenSurfacePreflight Result(KsProcgenSurfacePreflightStatus status, string reason,
            ItemPlacerComponent? tracker = null, PlaceableSurfaceComponent? surface = null) =>
            new(status, reason, tracker != null, tracker?.MaxEntities, tracker?.PlacedEntities.Count ?? 0,
                surface?.PlaceCentered ?? false, surface?.PositionOffset ?? Vector2.Zero);

        if (slotId != null && (string.IsNullOrWhiteSpace(slotId) || slotId.Length > 256))
            return Result(KsProcgenSurfacePreflightStatus.InvalidInput, "InvalidSurfaceSlot");
        if (!Ready(subjectUid) || !Ready(targetUid))
            return Result(KsProcgenSurfacePreflightStatus.InvalidInput, "SurfaceEntityNotReady");
        if (subjectUid == targetUid)
            return Result(KsProcgenSurfacePreflightStatus.Rejected, "SurfaceSelfPlacement");
        if (slotId != null)
            return Result(KsProcgenSurfacePreflightStatus.UnsupportedContent, "NamedSurfaceSlotUnsupported");
        if (_containerSystem.TryGetContainingContainer(subjectUid, out _) ||
            _containerSystem.TryGetContainingContainer(targetUid, out _))
            return Result(KsProcgenSurfacePreflightStatus.Rejected, "SurfaceEntityContained");
        if (!TryComp<ItemComponent>(subjectUid, out _) || !TryComp<TransformComponent>(subjectUid, out var subjectTransformComponent) ||
            subjectTransformComponent.Anchored)
            return Result(KsProcgenSurfacePreflightStatus.Rejected, "SurfaceSubjectNotDroppable");
        if (!TryComp<PlaceableSurfaceComponent>(targetUid, out var surfaceComponent))
            return Result(KsProcgenSurfacePreflightStatus.Rejected, "MissingLiveSurface");
        if (!float.IsFinite(surfaceComponent.PositionOffset.X) || !float.IsFinite(surfaceComponent.PositionOffset.Y))
            return Result(KsProcgenSurfacePreflightStatus.InvalidInput, "InvalidSurfaceOffset", surface: surfaceComponent);
        TryComp<ItemPlacerComponent>(targetUid, out var trackerComponent);
        if (!surfaceComponent.IsPlaceable)
            return Result(KsProcgenSurfacePreflightStatus.Rejected, "LiveSurfaceDisabled", tracker: trackerComponent, surface: surfaceComponent);
        if (trackerComponent != null)
        {
            var trackedEntities = trackerComponent.PlacedEntities;
            if (trackedEntities.Count > 256)
                return Result(KsProcgenSurfacePreflightStatus.BudgetExceeded, "SurfaceTrackerBudget", tracker: trackerComponent, surface: surfaceComponent);
            if (trackedEntities.Contains(subjectUid))
                return Result(KsProcgenSurfacePreflightStatus.Rejected, "SurfaceSubjectAlreadyTracked", tracker: trackerComponent, surface: surfaceComponent);
            if (trackerComponent.MaxEntities > 0 && (uint) trackedEntities.Count >= trackerComponent.MaxEntities)
                return Result(KsProcgenSurfacePreflightStatus.Rejected, "SurfaceTrackerFull", tracker: trackerComponent, surface: surfaceComponent);
            if (!_whitelistSystem.CheckBoth(subjectUid, whitelist: trackerComponent.Whitelist))
                return Result(KsProcgenSurfacePreflightStatus.Rejected, "SurfaceTrackingWhitelistDenied", tracker: trackerComponent, surface: surfaceComponent);
        }
        return Result(KsProcgenSurfacePreflightStatus.Candidate, trackerComponent == null ?
            "SurfaceDropCandidate" : "TrackedSurfaceCandidate", tracker: trackerComponent, surface: surfaceComponent);
    }

    private bool Ready(EntityUid uid) => TryComp<MetaDataComponent>(uid, out var metadataComponent) &&
        metadataComponent.EntityLifeStage is >= EntityLifeStage.Initialized and < EntityLifeStage.Terminating &&
        !EntityManager.IsQueuedForDeletion(uid);
}
