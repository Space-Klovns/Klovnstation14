using System.Linq;
using System.Threading;
using Content.Shared._KS14.Procedural;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenLayoutInstantiationSystem
{
    /// <summary>Finish selection and native source preparation before any map/grid/member allocation.</summary>
    internal bool TryPrepare(KsProcgenProject project, string areaId, KsProcgenRoomMapManifest source,
        KsProcgenRoomEntityExtraction extraction, int maximumMembers, out PreparedLayout? plan, out KsProcgenIssue? issue,
        KsProcgenLayoutInstantiationBudgets? budgets = null, CancellationToken cancellationToken = default)
    {
        plan = null; issue = null; budgets ??= new();
        if (project == null || source == null || extraction == null || budgets.Packing == null ||
            budgets.MaximumRoomInstances is < 0 or > 512 || budgets.MaximumTerrainCells is < 0 or > 65_536 ||
            budgets.MaximumSourceTileProbes is < 0 or > 1_048_576 || budgets.MaximumImageCharacters is < 0 or > 67_108_864 ||
            budgets.MaximumEligibilityOrientationProbes is < 0 or > KsProcgenPackingEligibility.MaximumOrientations ||
            budgets.MaximumEligibilityMemberProbes is < 0 or > 65_536 ||
            maximumMembers is < 0 or > KsProcgenPreviewOperationSystem.MaximumAllocationsPerStage)
            return Reject("InvalidLayoutInstantiationInput", "Source layout planning requires bounded captured inputs and shared allowances.", out issue);
        if (source.ProjectDeclarationHash != project.DeclarationHash || source.DocumentId != project.DocumentId ||
            source.Revision != project.Revision || extraction.SourceContentHash != source.ContentHash)
            return Reject("LayoutInstantiationSourceMismatch", "Project, room source and entity extraction must belong to the same captured revision.", out issue);
        if (!project.TryArea(areaId, out var area) || area!.Library == null)
            return Reject("LayoutInstantiationLibraryRequired", "The requested project area must name a room library.", out issue);
        var request = area.Request.CopyRequest();
        if (request.Mode is not (KsProcgenMode.Prefabs or KsProcgenMode.Hybrid) ||
            request.Shape.PreservedCells.Count != 0 || request.ConstantRegions.Count != 0)
            return Reject("LayoutInstantiationContextUnsupported", "This owned source adapter needs a prefab/hybrid area without uncaptured constants.", out issue);
        // Freeze caller configuration before any serializer or startup callback can mutate it.
        var maximumRooms = budgets.MaximumRoomInstances; var maximumTiles = budgets.MaximumTerrainCells;
        var maximumProbes = budgets.MaximumSourceTileProbes; var maximumImages = budgets.MaximumImageCharacters;
        var maximumEligibilityOrientations = budgets.MaximumEligibilityOrientationProbes;
        var maximumEligibilityMembers = budgets.MaximumEligibilityMemberProbes;
        var packingBudgets = new KsProcgenPackingBudgets
        {
            MaxSearchNodes = budgets.Packing.MaxSearchNodes, MaxCandidateProbes = budgets.Packing.MaxCandidateProbes,
            MaxSearchDepth = budgets.Packing.MaxSearchDepth, MaxRouteExpandedCells = budgets.Packing.MaxRouteExpandedCells,
            PreferredPrefabCoveragePercent = budgets.Packing.PreferredPrefabCoveragePercent,
        };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryEligibility(area.Library, project.RoomBindings, extraction, maximumEligibilityOrientations, maximumEligibilityMembers,
                    out var eligibility, out issue, cancellationToken)) return false;
            var packing = KsProcgenPackingPlanner.Plan(request, area.Library.Families, budgets: packingBudgets, eligibility: eligibility!.Packing);
            if (packing.Status != KsProcgenPackingStatus.GeometryReady)
            { issue = packing.Issue ?? new("LayoutInstantiationNoPacking", "No complete geometric layout was selected."); return false; }
            var claims = packing.CellClaims.ToDictionary(entry => entry.Cell, entry => entry.Claim);
            var owners = packing.CellClaims.Where(entry => entry.Claim.Disposition == KsProcgenCellDisposition.Prefab)
                .GroupBy(entry => entry.Claim.OwnerId).ToDictionary(group => group.Key,
                    group => group.Select(entry => entry.Cell).ToHashSet(), StringComparer.Ordinal);
            var usedOwners = new HashSet<string>(StringComparer.Ordinal);
            var prepared = new List<PreparedRoom>(); var tileCount = 0; var probes = 0; var entities = 0;
            long images = 0;
            foreach (var placement in packing.Placements)
            {
                var placementClaims = placement.Claims.ToDictionary(entry => entry.Cell, entry => entry.Claim);
                var bindings = project.RoomBindings.Where(binding => binding.FamilyId == placement.FamilyId && binding.OptionId == placement.OptionId)
                    .OrderBy(binding => binding.MemberId, StringComparer.Ordinal).ToArray();
                if (bindings.Length == 0 && placement.Claims.Any(entry => entry.Claim.Disposition == KsProcgenCellDisposition.Prefab)) return Reject("LayoutInstantiationMissingBindings", "Every selected alternative needs complete source bindings.", out issue);
                foreach (var binding in bindings)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (prepared.Count >= maximumRooms) return Reject("LayoutInstantiationRoomBudget", "Selected room instances exceed their shared allowance.", out issue);
                    var origin = KsProcgenGeometry.TransformCell(binding.Origin, default, placement.Origin, placement.QuarterTurns);
                    var turns = (binding.QuarterTurns + placement.QuarterTurns) % 4;
                    if (!_terrain.TryPrepare(source, extraction, binding.RoomAssetId, origin, turns, out var terrain, out issue,
                            maximumTiles: maximumTiles - tileCount, maximumSourceTileProbes: maximumProbes - probes,
                            cancellationToken: cancellationToken)) return false;
                    var cells = terrain!.Tiles.Select(tile => tile.Cell).ToHashSet();
                    if (cells.Count == 0 || !claims.TryGetValue(terrain.Tiles[0].Cell, out var claim) ||
                        claim.Disposition != KsProcgenCellDisposition.Prefab || !owners.TryGetValue(claim.OwnerId, out var ownerCells) ||
                        !cells.SetEquals(ownerCells) || !usedOwners.Add(claim.OwnerId) ||
                        cells.Any(cell => !placementClaims.TryGetValue(cell, out var placedClaim) || placedClaim != claim))
                        return Reject("LayoutInstantiationBindingMismatch", "A copied source footprint does not exactly match its selected member reservation.", out issue);
                    var room = extraction.Rooms.Single(room => room.Room.RoomAssetId == binding.RoomAssetId);
                    entities += room.Entities.Count;
                    if (entities > maximumMembers)
                        return Reject("LayoutInstantiationAllocationBudget", "All selected source members exceed the remaining allocation allowance.", out issue);
                    if (!_rooms.TryPrepare(extraction, binding.RoomAssetId, origin, turns, room.Entities.Count,
                            out var entityPlan, out issue, maximumImageCharacters: maximumImages - (int) images,
                            cancellationToken: cancellationToken))
                    {
                        if (issue?.Code == "RoomInstantiationImageBudget")
                            return Reject("LayoutInstantiationImageBudget", "All selected copies exceed their shared image allowance.", out issue);
                        return false;
                    }
                    images += entityPlan!.ImageCharacters;
                    tileCount += terrain.Tiles.Count; probes += terrain.SourceTileProbes;
                    prepared.Add(new(placement, binding, claim.OwnerId, origin, turns, terrain, entityPlan));
                }
            }
            if (usedOwners.Count != owners.Count) return Reject("LayoutInstantiationMissingBindings", "Some selected room claims have no complete source binding.", out issue);
            if (!_definitions.Matches(extraction.Definitions, out issue)) return false;
            cancellationToken.ThrowIfCancellationRequested();
            plan = new(this, project.DocumentId, project.Revision, project.DeclarationHash, areaId, source.ContentHash,
                extraction, packing, prepared.ToArray(), entities, tileCount, probes, (int) images, eligibility!);
            return true;
        }
        catch (OperationCanceledException)
        { return Reject("LayoutInstantiationCancelled", "Source layout preparation was cancelled before writing.", out issue); }
        catch (Exception exception)
        {
            Log.Debug($"Selected layout preparation failed: {exception}");
            return Reject("LayoutInstantiationEngineFailure", "Selected layout source preparation failed before writing.", out issue);
        }
    }
}
