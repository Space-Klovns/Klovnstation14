using Content.Shared.Placeable;
using Content.Shared._KS14.Procedural;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._KS14.Procedural;

public enum KsProcgenSurfaceContactStatus : byte
{
    Observed,
    NoContact,
    Rejected,
    InvalidInput,
    BudgetExceeded,
}

/// <summary>Current physics/tracking observation, not mounting, support stability or operational access.</summary>
public sealed record KsProcgenSurfaceContact(
    KsProcgenSurfaceContactStatus Status, string Reason, int ExaminedRecords, int SoftContactRecords,
    int HardContactRecords, bool HasTracker, bool SubjectTracked, int TrackedEntities,
    uint? MaximumTrackedEntities, bool SurfaceEnabled)
{
    public bool PlacementVerified => false;
}

/// <summary>Reads actual contact records after an engine physics step; never steps or mutates the world.</summary>
public sealed partial class KsProcgenSurfaceContactSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;

    public KsProcgenSurfaceContact Check(EntityUid subjectUid, EntityUid surfaceUid, int maxContactRecords = 256)
    {
        var examined = 0;
        var soft = 0;
        var hard = 0;
        ItemPlacerComponent? trackerComponent = null;
        PlaceableSurfaceComponent? surfaceComponent = null;
        KsProcgenSurfaceContact Result(KsProcgenSurfaceContactStatus status, string reason)
        {
            var trackedEntities = trackerComponent?.PlacedEntities;
            return new(status, reason, examined, soft, hard, trackerComponent != null,
                trackedEntities?.Contains(subjectUid) ?? false, trackedEntities?.Count ?? 0,
                trackerComponent?.MaxEntities, surfaceComponent?.IsPlaceable ?? false);
        }

        if (maxContactRecords is < 1 or > 256 || subjectUid == surfaceUid || !Ready(subjectUid) || !Ready(surfaceUid))
            return Result(KsProcgenSurfaceContactStatus.InvalidInput, "InvalidSurfaceContactInput");
        if (!TryComp(surfaceUid, out surfaceComponent))
            return Result(KsProcgenSurfaceContactStatus.Rejected, "MissingLiveSurface");
        TryComp(surfaceUid, out trackerComponent);
        if (_containerSystem.TryGetContainingContainer(subjectUid, out _) ||
            _containerSystem.TryGetContainingContainer(surfaceUid, out _))
            return Result(KsProcgenSurfaceContactStatus.Rejected, "SurfaceContactContained");
        if (!TryComp<TransformComponent>(subjectUid, out var subjectTransformComponent) ||
            !TryComp<TransformComponent>(surfaceUid, out var surfaceTransformComponent) ||
            subjectTransformComponent.MapUid == null || subjectTransformComponent.MapUid != surfaceTransformComponent.MapUid)
            return Result(KsProcgenSurfaceContactStatus.Rejected, "SurfaceContactMapMismatch");
        if (!TryComp<PhysicsComponent>(subjectUid, out var subjectPhysicsComponent) || !subjectPhysicsComponent.CanCollide ||
            !TryComp<PhysicsComponent>(surfaceUid, out var surfacePhysicsComponent) || !surfacePhysicsComponent.CanCollide)
            return Result(KsProcgenSurfaceContactStatus.Rejected, "SurfaceContactPhysicsUnavailable");
        if (!TryComp<FixturesComponent>(subjectUid, out var subjectFixturesComponent) ||
            !TryComp<FixturesComponent>(surfaceUid, out var surfaceFixturesComponent))
            return Result(KsProcgenSurfaceContactStatus.Rejected, "SurfaceContactFixturesUnavailable");
        if (subjectFixturesComponent.Fixtures.Count > 64 || surfaceFixturesComponent.Fixtures.Count > 64 ||
            trackerComponent?.PlacedEntities.Count > 256)
            return Result(KsProcgenSurfaceContactStatus.BudgetExceeded, "SurfaceContactDeclarationBudget");
        var contacts = _physicsSystem.GetContacts((subjectUid, subjectFixturesComponent), includeDeleting: true);
        var subjectPose = _physicsSystem.GetPhysicsTransform(subjectUid, xform: subjectTransformComponent);
        var surfacePose = _physicsSystem.GetPhysicsTransform(surfaceUid, xform: surfaceTransformComponent);
        while (contacts.MoveNext(out var contact))
        {
            if (++examined > maxContactRecords)
                return Result(KsProcgenSurfaceContactStatus.BudgetExceeded, "SurfaceContactRecordBudget");
            if (contact.Deleting || !contact.IsTouching ||
                !((contact.EntityA == subjectUid && contact.EntityB == surfaceUid) ||
                  (contact.EntityB == subjectUid && contact.EntityA == surfaceUid)))
                continue;
            var subjectFixture = contact.EntityA == subjectUid ? contact.FixtureA : contact.FixtureB;
            var surfaceFixture = contact.EntityA == surfaceUid ? contact.FixtureA : contact.FixtureB;
            var subjectFixtureId = contact.EntityA == subjectUid ? contact.FixtureAId : contact.FixtureBId;
            var surfaceFixtureId = contact.EntityA == surfaceUid ? contact.FixtureAId : contact.FixtureBId;
            var subjectChild = contact.EntityA == subjectUid ? contact.ChildIndexA : contact.ChildIndexB;
            var surfaceChild = contact.EntityA == surfaceUid ? contact.ChildIndexA : contact.ChildIndexB;
            if (subjectFixture == null || surfaceFixture == null ||
                !subjectFixturesComponent.Fixtures.TryGetValue(subjectFixtureId, out var declaredSubjectFixture) ||
                !surfaceFixturesComponent.Fixtures.TryGetValue(surfaceFixtureId, out var declaredSurfaceFixture) ||
                !ReferenceEquals(subjectFixture, declaredSubjectFixture) || !ReferenceEquals(surfaceFixture, declaredSurfaceFixture) ||
                subjectChild < 0 || subjectChild >= subjectFixture.Shape.ChildCount ||
                surfaceChild < 0 || surfaceChild >= surfaceFixture.Shape.ChildCount)
                return Result(KsProcgenSurfaceContactStatus.Rejected, "SurfaceContactFixtureDrift");
            // Sleeping bodies may retain an old IsTouching record after a manual move. Disjoint
            // current bounds refute contact; intersecting bounds do not prove fresh exact overlap.
            if (!subjectFixture.Shape.ComputeAABB(subjectPose, subjectChild).Intersects(
                    surfaceFixture.Shape.ComputeAABB(surfacePose, surfaceChild)))
                continue;
            if (!KsProcgenConvexShapeOverlap.TryCheck(subjectFixture.Shape, subjectPose,
                    surfaceFixture.Shape, surfacePose, out var overlaps, out var shapeIssue))
                return Result(KsProcgenSurfaceContactStatus.Rejected, shapeIssue!);
            if (!overlaps)
                continue;
            if (contact.Hard)
                hard++;
            else
                soft++;
        }
        if (hard > 0)
            return Result(KsProcgenSurfaceContactStatus.Rejected, "SurfaceHardContactObserved");
        if (soft == 0)
            return Result(KsProcgenSurfaceContactStatus.NoContact, "SurfaceContactNotObserved");
        if (trackerComponent != null)
        {
            if (!_whitelistSystem.CheckBoth(subjectUid, whitelist: trackerComponent.Whitelist))
                return Result(KsProcgenSurfaceContactStatus.Rejected, "SurfaceTrackingWhitelistDenied");
            var trackedEntities = trackerComponent.PlacedEntities;
            if (!trackedEntities.Contains(subjectUid))
                return Result(KsProcgenSurfaceContactStatus.Rejected, "SurfaceTrackerMissingSubject");
            if (trackerComponent.MaxEntities > 0 && (uint) trackedEntities.Count > trackerComponent.MaxEntities)
                return Result(KsProcgenSurfaceContactStatus.Rejected, "SurfaceTrackerOverCapacity");
        }
        // A tracker reaching its limit disables new drops. Report that flag without treating it as
        // proof that an already tracked contact is invalid (or that a manually disabled surface is usable).
        return Result(KsProcgenSurfaceContactStatus.Observed, "SurfaceSoftContactObserved");
    }

    private bool Ready(EntityUid uid) => TryComp<MetaDataComponent>(uid, out var metadataComponent) &&
        metadataComponent.EntityLifeStage is >= EntityLifeStage.Initialized and < EntityLifeStage.Terminating &&
        !EntityManager.IsQueuedForDeletion(uid);
}
