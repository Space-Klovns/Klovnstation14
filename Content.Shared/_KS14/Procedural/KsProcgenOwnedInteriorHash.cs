using System.Globalization;
using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

internal static class KsProcgenOwnedInteriorHash
{
    internal static ulong Compute(KsProcgenRequest request, string themeId, KsProcgenGroupFloorSelection floor,
        KsProcgenGroupPartitionResult partition, KsProcgenThemeAssignmentResult themes, KsProcgenGroupWindowResult structure,
        IReadOnlyList<KsProcgenFurnishedRegion> furnishings, KsProcgenGroupLightingResult lighting,
        KsProcgenGroupInteriorResult access, IReadOnlyList<KsProcgenSizeMixOutcome> sizeMix, IReadOnlyList<KsProcgenOwnedFallback> fallbacks,
        KsProcgenGasBindingResult? gas = null, KsProcgenElectricalPlan? electrical = null)
    {
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-owned-interior-v10");
        // Utility reservations contribute only when the corresponding complete stage exists.
        if (electrical != null) { hash.AddString("electrical-plan-v1"); hash.AddString(electrical.PlanHash.ToString(CultureInfo.InvariantCulture)); }
        if (gas != null) { hash.AddString("gas-binding-v1"); hash.AddString(gas.BindingHash.ToString(CultureInfo.InvariantCulture)); }
        hash.AddString(request.RequestId); hash.AddInt(request.Seed); hash.AddString(themeId);
        foreach (var value in new[] { floor.SelectionHash, partition.PartitionHash, structure.WindowProposalHash, access.AccessHash })
            hash.AddString(value.ToString(CultureInfo.InvariantCulture));
        hash.AddInt(request.FallbackPolicy.AllowMergedPartition ? 1 : 0);
        hash.AddInt(request.FallbackPolicy.AllowRoomSizeShortfall ? 1 : 0);
        hash.AddInt(request.FallbackPolicy.AllowSparseFurnishing ? 1 : 0);
        hash.AddInt(request.FallbackPolicy.AllowSparseLighting ? 1 : 0);
        hash.AddInt(request.SizeMix.Count);
        foreach (var goal in request.SizeMix.OrderBy(goal => goal.Id, StringComparer.Ordinal))
        {
            hash.AddString(goal.Id); hash.AddInt(goal.MinCells); hash.AddInt(goal.MaxCells); hash.AddInt(goal.TargetCount);
        }
        hash.AddInt(themes.Regions.Count);
        foreach (var region in themes.Regions.OrderBy(region => region.Id, StringComparer.Ordinal))
        {
            hash.AddString(region.Id); hash.AddInt((int) region.Kind);
            var theme = region.Theme;
            foreach (var id in new[] { theme.ThemeId, theme.TilePackId, theme.TilePaletteId, theme.WallPackId, theme.WallFamilyId,
                         theme.LightingPackId, theme.LightFixtureId, theme.DominantEntityPackId }) hash.AddString(id ?? "");
            hash.AddInt(BitConverter.SingleToInt32Bits(theme.FurnishingDensity));
            hash.AddInt(theme.Content?.Goals.CoherentContents == true ? 1 : 0);
            hash.AddInt(theme.SupportingEntityPackIds.Count);
            foreach (var id in theme.SupportingEntityPackIds.OrderBy(id => id, StringComparer.Ordinal)) hash.AddString(id);
            hash.AddInt(region.UnplacedRequiredPacks.Count);
            foreach (var pack in region.UnplacedRequiredPacks.OrderBy(pack => pack.PackId, StringComparer.Ordinal))
            { hash.AddString(pack.PackId); hash.AddInt(pack.MinimumCount); }
        }
        hash.AddInt(furnishings.Count);
        foreach (var room in furnishings.OrderBy(room => room.RegionId, StringComparer.Ordinal))
        {
            var proposal = room.Proposal;
            hash.AddString(room.RegionId); hash.AddInt((int) proposal.Status);
            hash.AddInt(proposal.TargetDominantClusters); hash.AddInt(proposal.PlacedDominantClusters);
            hash.AddInt(proposal.PreferenceSearchTruncated ? 1 : 0); hash.AddInt(proposal.RelationPathSearchTruncated ? 1 : 0);
            hash.AddInt(proposal.Layered == null ? 0 : 1);
            if (proposal.Layered is { } layered)
            {
                hash.AddString("automatic-room-preferences-v1");
                hash.AddInt(layered.Placement.PreferenceScore.MissedRelations);
                hash.AddString(layered.Placement.PreferenceScore.DistancePenalty.ToString(CultureInfo.InvariantCulture));
                hash.AddInt(layered.Composition.Instances.Count);
                foreach (var instance in layered.Composition.Instances)
                {
                    hash.AddInt((int) instance.Origin); hash.AddString(instance.PackId); hash.AddString(instance.CoreId); hash.AddInt(instance.CopyIndex);
                    hash.AddString(instance.Assembly.AssemblyId); hash.AddString(instance.Assembly.VariantId); hash.AddString(instance.Assembly.AnchorMember);
                }
                hash.AddInt(layered.Placement.RootPreferenceOutcomes.Count);
                foreach (var outcome in layered.Placement.RootPreferenceOutcomes)
                {
                    hash.AddString(outcome.Subject); hash.AddString(outcome.Target);
                    hash.AddInt(outcome.MinimumDistance); hash.AddInt(outcome.MaximumDistance);
                    hash.AddString(outcome.Distance.ToString(CultureInfo.InvariantCulture));
                    hash.AddString(outcome.Penalty.ToString(CultureInfo.InvariantCulture));
                }
            }
            AddCells(proposal.ProtectedPassageCells);
            hash.AddInt(proposal.Entities.Count);
            foreach (var entity in proposal.Entities.OrderBy(entity => entity.Origin).ThenBy(entity => entity.PackId, StringComparer.Ordinal)
                         .ThenBy(entity => entity.CoreId, StringComparer.Ordinal).ThenBy(entity => entity.ClusterIndex)
                         .ThenBy(entity => entity.EntryId, StringComparer.Ordinal).ThenBy(entity => entity.Cell.Y).ThenBy(entity => entity.Cell.X))
            {
                foreach (var id in new[] { entity.PackId, entity.EntryId, entity.EntityId, entity.CoreId, entity.AssemblyId, entity.VariantId })
                    hash.AddString(id ?? "");
                hash.AddInt((int) entity.Origin); hash.AddInt((int) entity.Role); hash.AddInt((int) entity.Movement); AddCell(entity.Cell);
                AddCells(entity.OccupiedCells); hash.AddInt(entity.QuarterTurns); hash.AddInt(entity.ClusterIndex);
                AddNullableCell(entity.InteractionApproach); AddNullableCell(entity.DeclaredApproachLanding);
            }
            hash.AddInt(proposal.Omissions.Count);
            foreach (var omission in proposal.Omissions.OrderBy(item => item.PackId, StringComparer.Ordinal).ThenBy(item => item.Reason, StringComparer.Ordinal))
            { hash.AddString(omission.PackId); hash.AddString(omission.Reason); }
            hash.AddInt(proposal.RelationWitnesses.Count);
            foreach (var witness in proposal.RelationWitnesses.OrderBy(item => item.PackId, StringComparer.Ordinal)
                         .ThenBy(item => item.CoreId, StringComparer.Ordinal).ThenBy(item => item.ClusterIndex).ThenBy(item => item.RelationId, StringComparer.Ordinal))
            {
                foreach (var id in new[] { witness.PackId, witness.CoreId, witness.AssemblyId, witness.VariantId, witness.RelationId,
                             witness.SubjectId, witness.TargetId, witness.ReasonCode }) hash.AddString(id ?? "");
                hash.AddInt(witness.ClusterIndex); hash.AddInt((int) witness.Kind); hash.AddInt((int) witness.Severity); hash.AddInt((int) witness.State);
                AddNullableCell(witness.SubjectCell); AddNullableCell(witness.TargetCell); AddNullableCell(witness.FirstBackingWall);
                AddNullableCell(witness.SecondBackingWall); AddNullableCell(witness.InteractionApproach);
                AddOptionalInt(witness.MinimumDistance); AddOptionalInt(witness.MaximumDistance); AddOptionalInt(witness.PathDistance);
                hash.AddInt(witness.CleanPath.Cells.Count);
                foreach (var cell in witness.CleanPath.Cells) AddCell(cell); // Path order is semantic.
            }
        }
        hash.AddInt(lighting.Regions.Count);
        foreach (var room in lighting.Regions.OrderBy(room => room.RegionId, StringComparer.Ordinal))
        {
            hash.AddString(room.RegionId); hash.AddInt((int) room.Proposal.Status);
            hash.AddInt(room.Proposal.TotalFloorCells); hash.AddInt(room.Proposal.EstimatedCoveredCells);
            hash.AddInt(BitConverter.SingleToInt32Bits(room.Proposal.RequestedCoverage));
            hash.AddInt(room.Proposal.Lights.Count);
            foreach (var light in room.Proposal.Lights.OrderBy(light => light.Cell.Y).ThenBy(light => light.Cell.X))
            { AddCell(light.Cell); hash.AddString(light.EntityId); hash.AddString(light.FixtureId); hash.AddInt((int) light.DeclaredSupply); }
        }
        hash.AddInt(sizeMix.Count);
        foreach (var outcome in sizeMix.OrderBy(outcome => outcome.GoalId, StringComparer.Ordinal))
        { hash.AddString(outcome.GoalId); hash.AddInt(outcome.RequestedCount); hash.AddInt(outcome.AchievedCount); }
        hash.AddInt(fallbacks.Count);
        foreach (var fallback in fallbacks.OrderBy(fallback => fallback.Code, StringComparer.Ordinal).ThenBy(fallback => fallback.RegionId, StringComparer.Ordinal))
        { hash.AddString(fallback.Code); hash.AddString(fallback.RegionId ?? ""); }
        void AddCell(Vector2i cell) { hash.AddInt(cell.X); hash.AddInt(cell.Y); }
        void AddCells(IEnumerable<Vector2i> cells)
        {
            var ordered = KsProcgenGeometry.SortCells(cells); hash.AddInt(ordered.Count);
            foreach (var cell in ordered) AddCell(cell);
        }
        void AddNullableCell(Vector2i? cell) { hash.AddInt(cell.HasValue ? 1 : 0); if (cell.HasValue) AddCell(cell.Value); }
        void AddOptionalInt(int? value) { hash.AddInt(value.HasValue ? 1 : 0); if (value.HasValue) hash.AddInt(value.Value); }
        return hash.Value;
    }
}
