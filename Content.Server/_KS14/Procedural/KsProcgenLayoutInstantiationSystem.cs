using System.Threading;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;

namespace Content.Server._KS14.Procedural;

internal sealed record KsProcgenLayoutInstantiationBudgets
{
    public KsProcgenPackingBudgets Packing { get; init; } = new();
    public int MaximumRoomInstances { get; init; } = 256;
    public int MaximumTerrainCells { get; init; } = 65_536;
    public int MaximumSourceTileProbes { get; init; } = 262_144;
    public int MaximumImageCharacters { get; init; } = 4_194_304;
    public int MaximumEligibilityOrientationProbes { get; init; } = 1024;
    public int MaximumEligibilityMemberProbes { get; init; } = 16_384;
}

internal sealed record KsProcgenSelectedRoomInstance(string OwnerId, string FamilyId, string OptionId,
    string MemberId, string AssetId, KsProcgenRoomInstance Instance);

/// <summary>Selected prefab portion only. Generated cells still need the composed hybrid planner.</summary>
internal sealed record KsProcgenInstantiatedLayout(KsProcgenPackingResult Packing,
    IReadOnlyList<KsProcgenSelectedRoomInstance> Rooms, IReadOnlyList<Vector2i> GeneratedCells,
    int SourceTileProbes, int ImageCharacters, KsProcgenNativeLayoutEligibility Eligibility);

/// <summary>Automatically fits and copies complete layout alternatives inside its caller's owned transaction.</summary>
public sealed partial class KsProcgenLayoutInstantiationSystem : EntitySystem
{
    [Dependency] private KsProcgenRoomInstantiationSystem _rooms = default!;
    [Dependency] private KsProcgenRoomTerrainSystem _terrain = default!;
    [Dependency] private KsProcgenPrototypeManifestSystem _definitions = default!;
    [Dependency] private KsProcgenPreviewOperationSystem _operations = default!;

    internal sealed record PreparedRoom(KsProcgenPlacementCandidate Placement, KsProcgenLayoutRoomBinding Binding,
        string OwnerId, Vector2i Origin, int Turns, KsProcgenRoomTerrainPlan Terrain, KsProcgenRoomInstantiationSystem.PreparedRoom Entities);

    internal bool TryInstantiate(KsProcgenProject project, string areaId, KsProcgenRoomMapManifest source,
        KsProcgenRoomEntityExtraction extraction, EntityUid frameUid, List<EntityUid> allocations,
        int maximumAllocations, out KsProcgenInstantiatedLayout? layout, out KsProcgenIssue? issue,
        KsProcgenLayoutInstantiationBudgets? budgets = null, CancellationToken cancellationToken = default)
    {
        layout = null; issue = null;
        if (!_operations.IsCapturing(allocations) || maximumAllocations is < 1 or > KsProcgenPreviewOperationSystem.MaximumAllocationsPerStage)
            return Reject("InvalidLayoutInstantiationInput", "Automatic layout copying requires an owned transaction and bounded allocations.", out issue);
        if (!_terrain.CheckContext(frameUid, allocations, maximumAllocations, out issue)) return false;
        var originalMapUid = Transform(frameUid).MapUid; var originalMapId = Transform(frameUid).MapID;
        if (!TryPrepare(project, areaId, source, extraction, maximumAllocations - allocations.Count, out var plan, out issue,
                budgets: budgets, cancellationToken: cancellationToken)) return false;
        if (!_terrain.CheckContext(frameUid, allocations, maximumAllocations, out issue)) return false;
        if (Transform(frameUid).MapUid != originalMapUid || Transform(frameUid).MapID != originalMapId)
            return Reject("LayoutInstantiationContextDrift", "Preparation changed the caller's layout frame.", out issue);
        return TryInstantiatePrepared(plan!, frameUid, allocations, maximumAllocations, out layout, out issue,
            cancellationToken: cancellationToken);
    }

    internal bool TryInstantiatePrepared(PreparedLayout plan, EntityUid frameUid, List<EntityUid> allocations,
        int maximumAllocations, out KsProcgenInstantiatedLayout? layout, out KsProcgenIssue? issue,
        CancellationToken cancellationToken = default)
    {
        layout = null; issue = null;
        if (plan == null) return Reject("InvalidPreparedLayout", "A private source layout plan is required.", out issue);
        return plan.TryApply(this, frameUid, allocations, maximumAllocations, out layout, out issue, cancellationToken);
    }

    private bool Apply(PreparedLayout plan, KsProcgenPackingResult packing, IReadOnlyList<PreparedRoom> prepared,
        KsProcgenRoomEntityExtraction extraction, EntityUid frameUid, List<EntityUid> allocations, int maximumAllocations,
        out KsProcgenInstantiatedLayout? layout, out KsProcgenIssue? issue, CancellationToken cancellationToken)
    {
        layout = null; issue = null;
        if (!_terrain.CheckContext(frameUid, allocations, maximumAllocations, out issue)) return false;
        var frameMapUid = Transform(frameUid).MapUid;
        var frameMapId = Transform(frameUid).MapID;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (plan.EntityCount > maximumAllocations - allocations.Count)
                return Reject("LayoutInstantiationAllocationBudget", "The prepared layout exceeds the remaining allocation allowance.", out issue);
            if (!plan.TryConsume(this))
                return Reject("PreparedLayoutConsumed", "A source layout plan can be materialized only once by its originating system.", out issue);
            if (!_definitions.Matches(extraction.Definitions, out issue)) return false;
            if (!_terrain.CheckContext(frameUid, allocations, maximumAllocations, out issue)) return false;
            if (Transform(frameUid).MapUid != frameMapUid || Transform(frameUid).MapID != frameMapId)
                return Reject("LayoutInstantiationContextDrift", "The source layout frame changed before copying.", out issue);
            if (plan.EntityCount > maximumAllocations - allocations.Count)
                return Reject("LayoutInstantiationAllocationBudget", "Preparation callbacks exhausted the remaining allocation allowance.", out issue);
            var instances = new List<KsProcgenSelectedRoomInstance>();
            foreach (var room in prepared)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_terrain.TryWrite(room.Terrain, frameUid, allocations, maximumAllocations, out issue,
                        cancellationToken: cancellationToken)) return false;
                if (!_rooms.TryInstantiatePrepared(room.Entities, frameUid, allocations, maximumAllocations,
                        out var members, out issue, cancellationToken: cancellationToken)) return false;
                instances.Add(new(room.OwnerId, room.Placement.FamilyId, room.Placement.OptionId, room.Binding.MemberId,
                    room.Binding.RoomAssetId, new(room.Terrain, members!, frameUid, room.Origin, room.Turns)));
            }
            if (!_definitions.Matches(extraction.Definitions, out issue)) return false;
            foreach (var room in instances)
            {
                if (!_rooms.VerifyGraph(extraction, room.AssetId, frameUid, room.Instance.Origin, room.Instance.QuarterTurns,
                        room.Instance.Members, out issue, cancellationToken: cancellationToken) ||
                    !_terrain.Verify(room.Instance.Terrain, frameUid, allocations, maximumAllocations, out issue,
                        cancellationToken: cancellationToken)) return false;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!_terrain.CheckContext(frameUid, allocations, maximumAllocations, out issue)) return false;
            if (Transform(frameUid).MapUid != frameMapUid || Transform(frameUid).MapID != frameMapId)
                return Reject("LayoutInstantiationContextDrift", "The selected layout frame moved to another map during copying.", out issue);
            layout = new(packing, instances.AsReadOnly(), plan.GeneratedCells,
                plan.SourceTileProbes, plan.ImageCharacters, plan.Eligibility); return true;
        }
        catch (OperationCanceledException)
        { return Reject("LayoutInstantiationCancelled", "Layout copying was cancelled; retire the owning transaction.", out issue); }
        catch (Exception exception)
        {
            Log.Debug($"Selected layout instantiation failed: {exception}");
            return Reject("LayoutInstantiationEngineFailure", "Selected layout copying failed; retire the owning transaction.", out issue);
        }
    }

    private static bool Reject(string code, string message, out KsProcgenIssue? issue)
    { issue = new(code, message); return false; }
}
