using System.Collections.Frozen;
using System.Linq;
using System.Numerics;
using Content.Shared.Wall;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

internal static partial class KsProcgenLayeredFurnishingPlanner
{
    internal static KsProcgenFurnishingResult Plan(IPrototypeManager manager, IComponentFactory factory,
        KsProcgenThemedRegion region, KsProcgenPartitionResult partition, IReadOnlySet<Vector2i> inspectedWalls,
        int seed, int maximumProbes, int maximumExpandedCells, int maximumRoomCells,
        IReadOnlyList<KsProcgenRoomCoreSelection>? infrastructure = null)
    {
        var probes = 0;
        var expanded = 0;
        var truncated = false;
        var preferenceTruncated = false;
        var targetClusters = 0;
        var omitted = new List<KsProcgenPackOmission>();
        var selections = new List<KsProcgenRoomCoreChoice>();
        KsProcgenRoomCoreSearchResult? accepted = null;
        KsProcgenFurnishingResult Fail(KsProcgenFurnishingStatus status, string code) => new()
        {
            Status = status, Issue = new(code, "Complete room furnishing did not validate."), CandidateProbes = probes,
            RelationPathExpandedCells = expanded, ProtectedPassageCells = KsProcgenReadOnly.Copy(region.ProtectedPassageCells),
        };
        KsProcgenFurnishingResult Empty() => new()
        {
            Status = omitted.Count == 0 ? KsProcgenFurnishingStatus.Proposed : KsProcgenFurnishingStatus.Sparse,
            Omissions = omitted.ToReadOnlyList(), CandidateProbes = probes, RelationPathExpandedCells = expanded,
            PreferenceSearchTruncated = truncated, ProtectedPassageCells = KsProcgenReadOnly.Copy(region.ProtectedPassageCells),
        };
        var packs = SelectedPacks(region).OrderBy(pack => pack.Id == region.Theme.DominantEntityPackId ? 0 : 1)
            .ThenBy(pack => pack.Id, StringComparer.Ordinal).ToArray();
        var hardContents = infrastructure is { Count: > 0 } || region.UnplacedRequiredPacks.Count > 0 ||
            packs.Any(pack => pack.Cores.Any(core => core.MinimumCount > 0));
        if (maximumProbes < 0 || maximumExpandedCells < 0 || maximumRoomCells < 0 || packs.Length > 16)
            return Fail(KsProcgenFurnishingStatus.InvalidInput, "OwnedLayeredInputBudget");
        if (region.Kind == KsProcgenZoneKind.Passage)
            return hardContents ? Fail(KsProcgenFurnishingStatus.MandatoryUnmet, "MandatoryPackInPassage") : Empty();
        if (region.FloorCells.Count > Math.Min(4096, maximumRoomCells))
        {
            if (hardContents) return Fail(KsProcgenFurnishingStatus.BudgetExceeded, "OwnedLayeredInputBudget");
            foreach (var pack in packs) omitted.Add(new(pack.Id, "FurnishingRoomCellBudget"));
            truncated = true;
            return Empty();
        }
        if (region.UnplacedRequiredPacks.Any(required => required.MinimumCount != 1 || !packs.Any(pack => pack.Id == required.PackId)))
            return Fail(KsProcgenFurnishingStatus.MandatoryUnmet, "UnsupportedMandatoryPackCount");
        if (infrastructure != null)
        {
            if (infrastructure.Count > 16 || infrastructure.Any(item => item.Origin != KsProcgenRoomMemberOrigin.Infrastructure))
                return Fail(KsProcgenFurnishingStatus.InvalidInput, "InvalidInfrastructureCores");
            foreach (var item in infrastructure) selections.Add(new(new[] { item }));
        }
        foreach (var pack in packs)
        {
            var requiredPack = region.UnplacedRequiredPacks.Where(minimum => minimum.PackId == pack.Id)
                .Select(minimum => minimum.MinimumCount).DefaultIfEmpty(0).Max();
            var requiredCopies = 0;
            foreach (var core in pack.Cores.Where(core => core.MinimumCount > 0).OrderBy(core => core.Id, StringComparer.Ordinal))
            for (var copy = 0; copy < core.MinimumCount; copy++)
            {
                if (selections.Count == 16) return Fail(KsProcgenFurnishingStatus.BudgetExceeded, "OwnedLayeredInstanceBudget");
                selections.Add(new(new[] { new KsProcgenRoomCoreSelection(pack.Id, core, copy) }));
                requiredCopies++;
            }
            if (requiredCopies >= requiredPack) continue;
            var options = KsProcgenCoreSelector.Order(pack.Cores.Where(core => core.MinimumCount == 0 && core.Weight > 0f).ToArray(),
                seed, region.Id, pack.Id, 0);
            if (options.Count == 0) return Fail(KsProcgenFurnishingStatus.MandatoryUnmet, "OwnedLayeredPackMinimumUnmet");
            if (selections.Count == 16) return Fail(KsProcgenFurnishingStatus.BudgetExceeded, "OwnedLayeredInstanceBudget");
            selections.Add(new(options.Select(core => new KsProcgenRoomCoreSelection(pack.Id, core, requiredCopies)).ToArray()));
        }
        var floor = region.FloorCells.ToFrozenSet();
        var walls = SupportWalls(floor, inspectedWalls.Concat(partition.WallCells));
        var room = new KsProcgenRoomAccessMask(floor, walls,
            FrozenSet<Vector2i>.Empty, FrozenSet<Vector2i>.Empty, region.ProtectedPassageCells.ToFrozenSet(), new Vector2(0.5f, 0.5f));
        if (room.Network.Count == 0) return Fail(KsProcgenFurnishingStatus.InvalidInput, "OwnedLayeredMissingNetwork");

        KsProcgenRoomCoreSearchResult Solve()
        {
            var remaining = maximumProbes - probes;
            var hasChoices = selections.Any(choice => choice.Options.Count > 1);
            var choiceAllowance = hasChoices ? Math.Min(65_536, remaining / 4) : 0;
            var variants = Math.Min(64, (remaining - choiceAllowance) / 3);
            var landings = Math.Min(65_536, (remaining - choiceAllowance) / 3);
            var placementAllowance = Math.Min(65_536, remaining - choiceAllowance - variants - landings);
            var preferenceBudget = new KsProcgenPreferenceBudget(maximumSelectionProbes: Math.Min(32, (choiceAllowance + variants) / 4),
                maximumPlacementProbes: Math.Min(128, placementAllowance / 4),
                maximumExpandedCells: Math.Min(2048, (maximumExpandedCells - expanded) / 4),
                maximumLandingProbes: Math.Min(128, landings / 4));
            var preferences = region.Theme.Content?.Goals.CoherentContents == true && region.Theme.DominantEntityPackId is { } dominant
                ? new KsProcgenRoomPreferencePolicy(dominant) : null;
            var result = hasChoices
                ? KsProcgenRoomCoreSearch.SearchChoices(manager, factory, region.Id, selections, room, seed,
                    maximumChoiceProbes: choiceAllowance, maximumVariantProbes: variants, maximumPlacementProbes: placementAllowance,
                    maximumExpandedCells: Math.Min(65_536, maximumExpandedCells - expanded), maximumLandingProbes: landings,
                    requireConnectedCleanFloor: true, preferences: preferences, preferenceBudget: preferenceBudget)
                : KsProcgenRoomCoreSearch.Search(manager, factory, region.Id, selections.Select(choice => choice.Options[0]).ToArray(), room, seed,
                    maximumVariantProbes: variants, maximumPlacementProbes: placementAllowance,
                    maximumExpandedCells: Math.Min(65_536, maximumExpandedCells - expanded), maximumLandingProbes: landings,
                    requireConnectedCleanFloor: true, preferences: preferences, preferenceBudget: preferenceBudget);
            probes += result.ChoiceProbes + result.VariantProbes + result.PlacementProbes + result.LandingProbes;
            expanded += result.ExpandedCells;
            preferenceTruncated |= result.PreferenceSearchTruncated;
            return result;
        }
        if (selections.Count > 0)
        {
            var required = Solve();
            if (required.Status != KsProcgenSupportedAccessStatus.Candidate)
                return Fail(required.Status == KsProcgenSupportedAccessStatus.BudgetExceeded ? KsProcgenFurnishingStatus.BudgetExceeded :
                    required.Status == KsProcgenSupportedAccessStatus.InvalidInput ? KsProcgenFurnishingStatus.InvalidInput :
                    KsProcgenFurnishingStatus.MandatoryUnmet, required.Issue!.Code);
            accepted = required;
        }

        bool ContainsCore(string packId, string coreId) => accepted?.Composition!.Bindings.Any(binding =>
            binding.Origin == KsProcgenRoomMemberOrigin.Authored && binding.PackId == packId && binding.CoreId == coreId) == true;
        bool HasDominant() => region.Theme.DominantEntityPackId is not { } dominant ||
            accepted?.Composition!.Bindings.Any(binding => binding.Origin == KsProcgenRoomMemberOrigin.Authored && binding.PackId == dominant) == true;
        IReadOnlyList<KsProcgenRoomMemberBinding> DominantInstances() => accepted?.Composition!.Bindings
            .Where(binding => binding.Origin == KsProcgenRoomMemberOrigin.Authored && binding.PackId == region.Theme.DominantEntityPackId)
            .GroupBy(binding => (binding.CoreId, binding.CopyIndex)).Select(group => group.First()).ToArray() ?? [];
        KsProcgenFurnishingResult? fatal = null;
        bool AddOptional(string packId, IReadOnlyList<KsProcgenResolvedEntityCore> cores, string failureCode)
        {
            if (selections.Count == 16)
            {
                truncated = true;
                omitted.Add(new(packId, "OwnedLayeredInstanceBudget"));
                return false;
            }
            // Authored identity is per core. Reserve copies in every still-possible hard alternative.
            var options = cores.Select(core => new KsProcgenRoomCoreSelection(packId, core,
                selections.SelectMany(choice => choice.Options).Where(item => item.Origin == KsProcgenRoomMemberOrigin.Authored &&
                    item.PackId == packId && item.Core.Id == core.Id).Select(item => item.CopyIndex).DefaultIfEmpty(-1).Max() + 1)).ToArray();
            selections.Add(new(options));
            var result = Solve();
            if (result.Status == KsProcgenSupportedAccessStatus.Candidate)
            {
                accepted = result;
                return true;
            }
            selections.RemoveAt(selections.Count - 1);
            if (result.Status is KsProcgenSupportedAccessStatus.InvalidInput or KsProcgenSupportedAccessStatus.UnsupportedContent)
            {
                fatal = Fail(KsProcgenFurnishingStatus.InvalidInput, result.Issue!.Code);
                return false;
            }
            truncated |= result.Status == KsProcgenSupportedAccessStatus.BudgetExceeded;
            omitted.Add(new(packId, result.Status == KsProcgenSupportedAccessStatus.BudgetExceeded ? result.Issue!.Code : failureCode));
            return false;
        }
        if (region.Theme.FurnishingDensity > 0f)
        {
            foreach (var pack in packs)
            {
                var cores = KsProcgenCoreSelector.Order(pack.Cores.Where(core => core.MinimumCount == 0 && core.Weight > 0f).ToArray(),
                    seed, region.Id, pack.Id, 0);
                if (pack.Id != region.Theme.DominantEntityPackId && !HasDominant())
                {
                    if (cores.Any(core => !ContainsCore(pack.Id, core.Id))) omitted.Add(new(pack.Id, "DominantPackUnavailable"));
                    continue;
                }
                foreach (var core in cores)
                {
                    if (ContainsCore(pack.Id, core.Id)) continue;
                    AddOptional(pack.Id, new[] { core }, "CoreCannotFit/" + core.Id);
                    if (fatal != null) return fatal;
                    if (truncated) break;
                }
                if (truncated) break;
            }
            var dominantPack = packs.SingleOrDefault(pack => pack.Id == region.Theme.DominantEntityPackId);
            var instances = DominantInstances();
            if (dominantPack != null && instances.Count > 0)
            {
                var bindings = accepted!.Composition!.Bindings.ToDictionary(binding => binding.MemberId, StringComparer.Ordinal);
                var areas = accepted.Placement!.Access!.Footprints.Where(footprint =>
                        bindings[footprint.MemberId].Origin == KsProcgenRoomMemberOrigin.Authored &&
                        bindings[footprint.MemberId].PackId == dominantPack.Id)
                    .GroupBy(footprint => (bindings[footprint.MemberId].CoreId, bindings[footprint.MemberId].CopyIndex))
                    .Select(group => group.SelectMany(footprint => footprint.Cells).Where(room.Floor.Contains).Distinct().Count())
                    .Where(count => count > 0).ToArray();
                // Wall-only groups do not create an artificial floor-density demand.
                targetClusters = areas.Length == 0 ? instances.Count : Math.Clamp((int) Math.Floor(
                    room.Floor.Count * region.Theme.FurnishingDensity / areas.Average()), 1, 8);
                var repeatable = dominantPack.Cores.Where(core => core.Weight > 0f).ToArray();
                repeatable = repeatable.Select(core => core with { Variants = core.Variants.Where(variant => variant.Members.Any(member =>
                    !variant.Relations.Any(relation => relation.Subject == member.Id &&
                        relation.Kind is KsProcgenRelationKind.OnSurface or KsProcgenRelationKind.InContainer) &&
                    manager.TryIndex<EntityPrototype>(member.Entry.Entity, out var prototype) &&
                    !prototype.TryComp<WallMountComponent>(out _, factory))).ToArray() })
                    .Where(core => core.Variants.Count > 0).ToArray();
                var floorTarget = areas.Length == 0 ? 0 : targetClusters;
                int FloorInstanceCount()
                {
                    var currentBindings = accepted!.Composition!.Bindings.ToDictionary(binding => binding.MemberId, StringComparer.Ordinal);
                    return accepted.Placement!.Access!.Footprints.Where(footprint =>
                            currentBindings[footprint.MemberId].Origin == KsProcgenRoomMemberOrigin.Authored &&
                            currentBindings[footprint.MemberId].PackId == dominantPack.Id && footprint.Cells.Any(room.Floor.Contains))
                        .Select(footprint => (currentBindings[footprint.MemberId].CoreId, currentBindings[footprint.MemberId].CopyIndex)).Distinct().Count();
                }
                while (!truncated && FloorInstanceCount() < floorTarget && repeatable.Length > 0)
                {
                    var options = KsProcgenCoreSelector.Order(repeatable, seed, region.Id, dominantPack.Id, DominantInstances().Count + 1);
                    if (!AddOptional(dominantPack.Id, options, "FurnishingDensityUnmet")) break;
                    if (fatal != null) return fatal;
                }
                if (fatal != null) return fatal;
                targetClusters = floorTarget + DominantInstances().Count - FloorInstanceCount();
            }
        }
        if (accepted == null) return Empty();
        return Project(accepted, room, region, omitted, probes, expanded, truncated, preferenceTruncated, targetClusters, DominantInstances().Count);
    }

    private static KsProcgenFurnishingResult Project(KsProcgenRoomCoreSearchResult result, KsProcgenRoomAccessMask room,
        KsProcgenThemedRegion region, List<KsProcgenPackOmission> omitted, int probes, int expanded, bool truncated, bool preferenceTruncated,
        int targetClusters, int placedClusters)
    {
        var assembly = result.Composition!.Assembly!;
        var bindings = result.Composition.Bindings.ToDictionary(binding => binding.MemberId, StringComparer.Ordinal);
        var definitions = assembly.Members.ToDictionary(member => member.Id, StringComparer.Ordinal);
        var footprints = result.Placement!.Access!.Footprints.ToDictionary(footprint => footprint.MemberId, StringComparer.Ordinal);
        var access = result.Placement.Access.Access.ToDictionary(item => item.MemberId, StringComparer.Ordinal);
        var clusters = new Dictionary<(KsProcgenRoomMemberOrigin Origin, string PackId, string CoreId, int CopyIndex), int>();
        var packCounts = new Dictionary<(KsProcgenRoomMemberOrigin Origin, string PackId), int>();
        foreach (var instance in result.Composition.Bindings.GroupBy(binding => (binding.Origin, binding.PackId, binding.CoreId, binding.CopyIndex)))
        {
            var pack = (instance.Key.Origin, instance.Key.PackId);
            var index = packCounts.GetValueOrDefault(pack);
            clusters.Add(instance.Key, index);
            packCounts[pack] = index + 1;
        }
        var entities = new List<KsProcgenEntityProposal>();
        foreach (var pose in result.Placement.Access.Poses!.Members.Where(pose => pose.Support.Layer == KsProcgenPlacementLayer.Floor))
        {
            var id = pose.Support.MemberId;
            var binding = bindings[id];
            var entry = definitions[id].Entry;
            var relative = pose.Position - room.CellCenterOrigin;
            entities.Add(new(binding.PackId, binding.SourceMemberId, entry.Entity, entry.Role, entry.Movement,
                new((int) relative.X, (int) relative.Y), pose.QuarterTurns,
                access.TryGetValue(id, out var approach) ? approach.Approach : null,
                ClusterIndex: clusters[(binding.Origin, binding.PackId, binding.CoreId, binding.CopyIndex)],
                Footprint: new(footprints[id].Cells), CoreId: binding.CoreId,
                AssemblyId: binding.AssemblyId, VariantId: binding.VariantId) { Origin = binding.Origin });
        }
        var floorEntities = entities.ToReadOnlyList();
        var relations = result.Placement.Access.Relations;
        return new()
        {
            Status = omitted.Count > 0 || truncated || !result.Placement.PreferenceScore.Satisfied || relations.Any(witness => witness.State == KsProcgenConstraintState.Missed)
                ? KsProcgenFurnishingStatus.Sparse : KsProcgenFurnishingStatus.Proposed,
            Entities = floorEntities, Layered = new(result, room, floorEntities), Omissions = omitted.ToReadOnlyList(),
            CandidateProbes = probes, RelationPathExpandedCells = expanded, PreferenceSearchTruncated = truncated || preferenceTruncated,
            ProtectedPassageCells = KsProcgenReadOnly.Copy(region.ProtectedPassageCells), RelationWitnesses = relations,
            TargetDominantClusters = targetClusters, PlacedDominantClusters = placedClusters,
        };
    }
}
