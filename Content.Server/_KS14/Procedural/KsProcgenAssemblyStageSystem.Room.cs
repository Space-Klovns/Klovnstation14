using System.Linq;
using System.Threading;
using Content.Shared._KS14.Procedural;

namespace Content.Server._KS14.Procedural;

public sealed record KsProcgenRoomAssemblyStageResult(KsProcgenAssemblyStageStatus Status,
    KsProcgenIssue? Issue, KsProcgenAssemblyStage? Stage, KsProcgenRoomCoreSearchResult? Search);

public sealed partial class KsProcgenAssemblyStageSystem
{
    /// <summary>
    /// Jointly selects complete room cores before allocating one owned support-only preview.
    /// Reuses the internally computed access witness without refunding or repeating search work.
    /// Spatial engine validation and publication retain the existing staging gates.
    /// </summary>
    public KsProcgenRoomAssemblyStageResult TrySearchRoomStage(string roomId,
        IReadOnlyList<KsProcgenRoomCoreSelection> selections, KsProcgenRoomAccessMask accessMask, int seed,
        CancellationToken cancellationToken = default, int maxMembers = 64,
        int maxSpawnedEntities = MaximumSpawnedEntitiesPerStage,
        int maximumVariantProbes = 64, int maximumPlacementProbes = 4096,
        int maximumAccessExpandedCells = 4096, int maximumLandingProbes = 4096,
        string? floorTileId = null, string wallPrototypeId = "WallSolid", int maxTerrainCells = 4096)
    {
        KsProcgenRoomAssemblyStageResult Fail(KsProcgenAssemblyStageStatus status, string code) =>
            new(status, new(code, "Room support preview did not pass preallocation checks."), null, null);
        if (_operationSystem.Active)
            return Fail(KsProcgenAssemblyStageStatus.InvalidInput, "AssemblyStageOperationActive");
        if (maxMembers is < 1 or > 64 || maxSpawnedEntities is < 1 or > MaximumSpawnedEntitiesPerStage)
            return Fail(KsProcgenAssemblyStageStatus.InvalidInput, "InvalidAssemblyStageBudget");
        if (cancellationToken.IsCancellationRequested)
            return Fail(KsProcgenAssemblyStageStatus.Cancelled, "AssemblyStageCancelled");
        if (_stages.Count >= MaximumRetainedStages)
            return Fail(KsProcgenAssemblyStageStatus.BudgetExceeded, "AssemblyStageRetentionBudget");
        KsProcgenRoomTerrain? terrain = null;
        if (floorTileId != null)
        {
            var terrainIssue = PrepareTerrain(accessMask, floorTileId, wallPrototypeId, maxTerrainCells, out terrain);
            if (terrainIssue != null)
                return Fail(terrainIssue == "RoomTerrainCellBudget" ? KsProcgenAssemblyStageStatus.BudgetExceeded :
                    KsProcgenAssemblyStageStatus.InvalidInput, terrainIssue);
        }
        var search = KsProcgenRoomCoreSearch.Search(ProtoMan, Factory, roomId, selections, accessMask, seed,
            maximumVariantProbes: maximumVariantProbes, maximumPlacementProbes: maximumPlacementProbes,
            maximumExpandedCells: maximumAccessExpandedCells, maximumLandingProbes: maximumLandingProbes);
        if (search.Status != KsProcgenSupportedAccessStatus.Candidate)
            return new(search.Status switch
            {
                KsProcgenSupportedAccessStatus.InvalidInput => KsProcgenAssemblyStageStatus.InvalidInput,
                KsProcgenSupportedAccessStatus.UnsupportedContent => KsProcgenAssemblyStageStatus.UnsupportedContent,
                KsProcgenSupportedAccessStatus.BudgetExceeded => KsProcgenAssemblyStageStatus.BudgetExceeded,
                _ => KsProcgenAssemblyStageStatus.Rejected,
            }, search.Issue, null, search);
        var assembly = search.Composition!.Assembly!;
        if (assembly.Members.Count > maxMembers || assembly.Members.Count + 1 +
            (terrain == null ? 0 : 1 + terrain.Walls.Count) > maxSpawnedEntities)
            return new(KsProcgenAssemblyStageStatus.BudgetExceeded,
                new(assembly.Members.Count > maxMembers ? "AssemblyStageMemberBudget" : "AssemblyStageSpawnBudget",
                    "Selected room contents exceed the owned allocation allowance."), null, search);
        var stage = StagePrepared(assembly, assembly.Members.Select(member => member.Id).ToArray(),
            search.Placement!.Access!.Poses!, search.Placement.Access, cancellationToken, maxSpawnedEntities,
            terrain: terrain);
        return new(stage.Status, stage.Issue, stage.Stage, search);
    }
}
