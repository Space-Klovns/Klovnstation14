using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server._KS14.Procedural;

internal enum KsProcgenAssemblyPlacementStatus : byte { InvalidInput, BudgetExceeded, Rejected }
internal sealed record KsProcgenAssemblyPlacementIssue(KsProcgenAssemblyPlacementStatus Status, string Code);

/// <summary>Places and inspects one assembly inside its caller's captured owned transaction.</summary>
public sealed partial class KsProcgenAssemblyPlacementSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private KsProcgenContainerPreflightSystem _containerPreflightSystem = default!;
    [Dependency] private KsProcgenSurfacePreflightSystem _surfacePreflightSystem = default!;
    [Dependency] private KsProcgenPreviewOperationSystem _previewOperationSystem = default!;

    internal KsProcgenAssemblyPlacementIssue? Place(KsProcgenAssemblyPosePlan poses, EntityUid frameUid,
        Dictionary<string, EntityUid> members, List<EntityUid> spawnedEntities, int maxSpawnedEntities,
        CancellationToken cancellationToken, Func<string, string?>? validatePrototype = null,
        Func<string, EntityCoordinates, EntityUid>? spawnCaptured = null)
    {
        if (!_previewOperationSystem.IsCapturing(spawnedEntities) || members.Count != 0 ||
            poses.Status != KsProcgenAssemblySupportStatus.NeedsEngineValidation || poses.Members.Count is < 1 or > 64 ||
            maxSpawnedEntities < 1 || !spawnedEntities.Contains(frameUid) || !Exists(frameUid) || Terminating(frameUid))
            return Failure(KsProcgenAssemblyPlacementStatus.InvalidInput, "AssemblyStagePlacementContext");
        foreach (var pose in poses.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (spawnedEntities.Count >= maxSpawnedEntities)
                return Failure(KsProcgenAssemblyPlacementStatus.BudgetExceeded, "AssemblyStageSpawnBudget");
            var member = pose.Support;
            var prototypeIssue = validatePrototype?.Invoke(member.EntityPrototypeId);
            if (prototypeIssue != null) return Failure(KsProcgenAssemblyPlacementStatus.Rejected, prototypeIssue);
            // Members start in the owned map/grid frame; insertion establishes container parenting.
            var coordinates = new EntityCoordinates(frameUid, pose.Position);
            var memberUid = spawnCaptured != null ? spawnCaptured(member.EntityPrototypeId, coordinates) : Spawn(member.EntityPrototypeId, coordinates);
            members.Add(member.MemberId, memberUid);
            prototypeIssue = validatePrototype?.Invoke(member.EntityPrototypeId);
            if (prototypeIssue != null) return Failure(KsProcgenAssemblyPlacementStatus.Rejected, prototypeIssue);
            cancellationToken.ThrowIfCancellationRequested();
            if (spawnedEntities.Count > maxSpawnedEntities)
                return Failure(KsProcgenAssemblyPlacementStatus.BudgetExceeded, "AssemblyStageSpawnBudget");
            if (member.Layer == KsProcgenPlacementLayer.Container)
            {
                var parentUid = members[member.ParentMemberId!];
                var preflight = _containerPreflightSystem.Check(memberUid, parentUid, member.SlotId!);
                cancellationToken.ThrowIfCancellationRequested();
                if (spawnedEntities.Count > maxSpawnedEntities)
                    return Failure(KsProcgenAssemblyPlacementStatus.BudgetExceeded, "AssemblyStageSpawnBudget");
                if (preflight.Status != KsProcgenContainerPreflightStatus.Eligible)
                    return Failure(KsProcgenAssemblyPlacementStatus.Rejected, preflight.Reason);
                var inserted = preflight.UsesItemSlot ?
                    InsertSlotWithoutInteractionAudio(parentUid, member.SlotId!, memberUid) :
                    _containerSystem.TryGetContainer(parentUid, member.SlotId!, out var container) &&
                    _containerSystem.Insert(memberUid, container, force: false);
                cancellationToken.ThrowIfCancellationRequested();
                if (spawnedEntities.Count > maxSpawnedEntities)
                    return Failure(KsProcgenAssemblyPlacementStatus.BudgetExceeded, "AssemblyStageSpawnBudget");
                if (!inserted || !Membership(memberUid, parentUid, member.SlotId!))
                    return Failure(KsProcgenAssemblyPlacementStatus.Rejected, "AssemblyStageInsertionFailed");
            }
            else
            {
                if (Transform(memberUid).NoLocalRotation && pose.LocalQuarterTurns != 0)
                    return Failure(KsProcgenAssemblyPlacementStatus.Rejected, "AssemblyStageRotationUnsupported");
                if (member.Layer == KsProcgenPlacementLayer.Surface)
                {
                    // Grid spawning can auto-anchor an otherwise droppable item (for example a
                    // microwave). The declared surface layer requires the ordinary unanchored
                    // drop state; preserve anchoring for floor-root structures.
                    if (Transform(memberUid).Anchored) _transformSystem.Unanchor(memberUid);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (spawnedEntities.Count > maxSpawnedEntities)
                        return Failure(KsProcgenAssemblyPlacementStatus.BudgetExceeded, "AssemblyStageSpawnBudget");
                    var preflight = _surfacePreflightSystem.Check(memberUid, members[member.ParentMemberId!]);
                    if (preflight.Status != KsProcgenSurfacePreflightStatus.Candidate)
                        return Failure(preflight.Status == KsProcgenSurfacePreflightStatus.BudgetExceeded ?
                            KsProcgenAssemblyPlacementStatus.BudgetExceeded : KsProcgenAssemblyPlacementStatus.Rejected, preflight.Reason);
                }
                _transformSystem.SetCoordinates(memberUid, new EntityCoordinates(frameUid, pose.LocalPosition));
                _transformSystem.SetLocalRotation(memberUid, KsProcgenRotation.ToEngineAngle(pose.LocalQuarterTurns));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return spawnedEntities.Count > maxSpawnedEntities
            ? Failure(KsProcgenAssemblyPlacementStatus.BudgetExceeded, "AssemblyStageSpawnBudget") : null;
    }

    internal string? MembersIssue(KsProcgenAssemblyPosePlan poses, IReadOnlyDictionary<string, EntityUid> members,
        EntityUid frameUid, bool initialized)
    {
        if (members.Count != poses.Members.Count)
            return "AssemblyStageMemberDrift";
        foreach (var pose in poses.Members)
        {
            var member = pose.Support;
            var memberUid = members[member.MemberId];
            if (!TryComp(memberUid, out MetaDataComponent? metadataComponent) ||
                metadataComponent.EntityPrototype?.ID != member.EntityPrototypeId ||
                metadataComponent.EntityLifeStage != (initialized ?
                    EntityLifeStage.MapInitialized : EntityLifeStage.Initialized) ||
                EntityManager.IsQueuedForDeletion(memberUid) || !TryComp(memberUid, out TransformComponent? transformComponent))
                return "AssemblyStageMemberDrift";
            var parentUid = pose.CoordinateParentMemberId == null ? frameUid : members[pose.CoordinateParentMemberId];
            if (transformComponent.ParentUid != parentUid || transformComponent.LocalPosition != pose.LocalPosition ||
                transformComponent.LocalRotation != KsProcgenRotation.ToEngineAngle(pose.LocalQuarterTurns))
                return "AssemblyStagePoseDrift";
            if (member.Layer == KsProcgenPlacementLayer.Container)
            {
                if (!Membership(memberUid, parentUid, member.SlotId!))
                    return "AssemblyStageMembershipDrift";
            }
            else if (_containerSystem.TryGetContainingContainer(memberUid, out _))
                return "AssemblyStageUnexpectedContainment";
            if (member.Layer != KsProcgenPlacementLayer.Surface)
                continue;
            var supportUid = members[member.ParentMemberId!];
            var preflight = _surfacePreflightSystem.Check(memberUid, supportUid);
            if (preflight.Status != KsProcgenSurfacePreflightStatus.Candidate)
                return preflight.Reason;
            var offset = preflight.Centered ? preflight.PositionOffset : Vector2.Zero;
            if (Transform(supportUid).LocalPosition + offset != pose.LocalPosition)
                return "AssemblyStageDropOffsetDrift";
        }
        foreach (var group in poses.Members.Where(pose => pose.Support.Layer == KsProcgenPlacementLayer.Surface)
                     .GroupBy(pose => pose.Support.ParentMemberId))
        {
            var preflight = _surfacePreflightSystem.Check(members[group.First().Support.MemberId], members[group.Key!]);
            if (preflight.MaximumTrackedEntities is > 0 &&
                (ulong) preflight.TrackedEntities + (ulong) group.Count() > (ulong) preflight.MaximumTrackedEntities.Value)
                return "AssemblyStageSurfaceCapacityConflict";
        }
        return null;
    }

    /// <summary>Loading is silent; native eligibility and container insertion events still run.</summary>
    private bool InsertSlotWithoutInteractionAudio(EntityUid parentUid, string containerId, EntityUid subjectUid)
    {
        return _itemSlotsSystem.TryGetSlot(parentUid, containerId, out var slot) &&
            _itemSlotsSystem.CanInsert(parentUid, subjectUid, user: null, slot) &&
            slot.ContainerSlot != null && _containerSystem.Insert(subjectUid, slot.ContainerSlot, force: false);
    }

    private bool Membership(EntityUid subjectUid, EntityUid parentUid, string containerId)
    {
        if (!_containerSystem.TryGetContainer(parentUid, containerId, out var container) || !container.Contains(subjectUid))
            return false;
        // Check retained filters without retrying insertion into a now occupied slot.
        if (TryComp<ItemSlotsComponent>(parentUid, out var slotsComponent) &&
            _itemSlotsSystem.TryGetSlot(parentUid, containerId, out var slot, component: slotsComponent) &&
            (!ReferenceEquals(slot.ContainerSlot, container) ||
                !_whitelistSystem.CheckBoth(subjectUid, blacklist: slot.Blacklist, whitelist: slot.Whitelist)))
            return false;
        return !Transform(subjectUid).Anchored;
    }

    private static KsProcgenAssemblyPlacementIssue Failure(KsProcgenAssemblyPlacementStatus status, string code) => new(status, code);
}
