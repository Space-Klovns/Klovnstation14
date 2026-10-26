using System.Linq;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenRoomCoreSelection(string PackId, KsProcgenResolvedEntityCore Core, int CopyIndex)
{
    public KsProcgenRoomMemberOrigin Origin { get; init; }
    public IReadOnlySet<Robust.Shared.Maths.Vector2i>? AllowedFloorCells { get; init; }
}

public sealed class KsProcgenRoomCoreSearchResult
{
    public KsProcgenSupportedAccessStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public KsProcgenRoomAssemblyComposition? Composition { get; init; }
    public KsProcgenSupportedAssemblySearchResult? Placement { get; init; }
    public KsProcgenAssemblyCapabilityReport? Capabilities { get; init; }
    public int VariantProbes { get; init; }
    public int ChoiceProbes { get; init; }
    public int PlacementProbes { get; init; }
    public int ExpandedCells { get; init; }
    public int LandingProbes { get; init; }
    public bool PreferenceSearchTruncated { get; init; }
    public bool EnginePlacementVerified => false;
}

/// <summary>
/// Joint bounded search over complete selected core variants and all their floor/support poses.
/// Required copies cannot disappear; support members remain attached to their own instance.
/// A candidate still requires initialized collision, mounting and operating engine validation.
/// </summary>
public static partial class KsProcgenRoomCoreSearch
{
    public static KsProcgenRoomCoreSearchResult Search(IPrototypeManager prototypeManager, IComponentFactory componentFactory,
        string roomId, IReadOnlyList<KsProcgenRoomCoreSelection> selections, KsProcgenRoomAccessMask room, int seed,
        int maximumVariantProbes = 64, int maximumPlacementProbes = 4096,
        int maximumExpandedCells = 4096, int maximumLandingProbes = 4096, bool requireConnectedCleanFloor = false,
        KsProcgenRoomPreferencePolicy? preferences = null, KsProcgenPreferenceBudget? preferenceBudget = null)
    {
        var variants = 0;
        var placements = 0;
        var expanded = 0;
        var landings = 0;
        preferenceBudget ??= new();
        KsProcgenRoomCoreSearchResult Fail(KsProcgenSupportedAccessStatus status, string code) => new()
        {
            Status = status, Issue = new(code, "Selected room cores have no accepted complete supported placement."),
            VariantProbes = variants, PlacementProbes = placements, ExpandedCells = expanded, LandingProbes = landings,
        };
        if (prototypeManager == null || componentFactory == null || string.IsNullOrWhiteSpace(roomId) || roomId.Length > 512 ||
            selections == null || selections.Count is < 1 or > 16 || !KsProcgenSupportedAccessPlanner.ValidMask(room) ||
            !preferenceBudget.Valid || preferences is { Valid: false } ||
            maximumVariantProbes is < 0 or > 64 || maximumPlacementProbes is < 0 or > 65_536 ||
            maximumExpandedCells is < 0 or > 65_536 || maximumLandingProbes is < 0 or > 65_536 ||
            selections.Any(selection => selection == null || selection.Core == null ||
                string.IsNullOrWhiteSpace(selection.PackId) || selection.PackId.Length > 256 ||
                string.IsNullOrWhiteSpace(selection.Core.Id) || selection.Core.Id.Length > 128 ||
                selection.CopyIndex is < 0 or > 63 || !Enum.IsDefined(selection.Origin) || selection.Core.MinimumCount is < 0 or > 64 ||
                selection.AllowedFloorCells is { Count: > 4096 } ||
                !float.IsFinite(selection.Core.Weight) || selection.Core.Weight < 0f ||
                selection.Core.Variants == null || selection.Core.Variants.Count is < 1 or > 17))
            return Fail(KsProcgenSupportedAccessStatus.InvalidInput, "InvalidRoomCoreSearchInput");
        if (selections.Select(selection => (selection.Origin, selection.PackId, selection.Core.Id, selection.CopyIndex)).Distinct().Count() != selections.Count ||
            selections.GroupBy(selection => (selection.Origin, selection.PackId, selection.Core.Id)).Any(group =>
                group.Count() < group.Max(selection => selection.Core.MinimumCount)))
            return Fail(KsProcgenSupportedAccessStatus.InvalidInput, "RoomCoreSelectionCountMismatch");
        var ordered = selections.OrderBy(selection => selection.Origin).ThenBy(selection => selection.PackId, StringComparer.Ordinal)
            .ThenBy(selection => selection.Core.Id, StringComparer.Ordinal).ThenBy(selection => selection.CopyIndex).ToArray();
        var instances = new List<KsProcgenRoomAssemblyInstance>();
        KsProcgenRoomCoreSearchResult? accepted = null;
        KsProcgenRoomCoreSearchResult? terminal = null;
        bool Visit(int index)
        {
            if (index < ordered.Length)
            {
                var selection = ordered[index];
                foreach (var assembly in selection.Core.Variants)
                {
                    instances.Add(new(selection.PackId, selection.Core.Id, selection.CopyIndex, assembly) { Origin = selection.Origin });
                    if (Visit(index + 1)) return true;
                    instances.RemoveAt(instances.Count - 1);
                    if (terminal != null) return false;
                }
                return false;
            }
            if (variants >= maximumVariantProbes || !preferenceBudget.ChargeSelection())
            {
                terminal = Fail(KsProcgenSupportedAccessStatus.BudgetExceeded, "RoomCoreVariantProbeBudget");
                return false;
            }
            variants++;
            var composition = KsProcgenRoomAssemblyComposer.Compose(roomId, instances);
            if (composition.Assembly == null)
            {
                if (composition.Issue!.Code != "RoomAssemblyCompositionBudget")
                    terminal = Fail(KsProcgenSupportedAccessStatus.InvalidInput, composition.Issue.Code);
                return false;
            }
            var assemblyDeclaration = composition.Assembly;
            var allowedRoots = new Dictionary<string, IReadOnlySet<Robust.Shared.Maths.Vector2i>>(StringComparer.Ordinal);
            foreach (var binding in composition.Bindings)
            {
                var source = ordered.Single(item => item.Origin == binding.Origin && item.PackId == binding.PackId &&
                    item.Core.Id == binding.CoreId && item.CopyIndex == binding.CopyIndex);
                if (source.AllowedFloorCells != null && !assemblyDeclaration.Relations.Any(relation => relation.Subject == binding.MemberId &&
                        relation.Kind is KsProcgenRelationKind.OnSurface or KsProcgenRelationKind.InContainer))
                    allowedRoots.Add(binding.MemberId, source.AllowedFloorCells);
            }
            if (!KsProcgenPrototypeCapabilityInspector.TryInspectAssembly(prototypeManager, componentFactory,
                    assemblyDeclaration, out var capabilities, out var issue))
            {
                terminal = Fail(KsProcgenSupportedAccessStatus.InvalidInput, issue!.Code);
                return false;
            }
            var placement = KsProcgenSupportedAssemblySearch.Search(assemblyDeclaration,
                assemblyDeclaration.Members.Select(member => member.Id).ToArray(), capabilities!, room, seed,
                maximumPlacementProbes: maximumPlacementProbes - placements,
                maximumExpandedCells: maximumExpandedCells - expanded,
                maximumLandingProbes: maximumLandingProbes - landings, allowedFloorCells: allowedRoots,
                requireConnectedCleanFloor: requireConnectedCleanFloor,
                rootPreferences: preferences?.Compile(composition, ordered, instances, capabilities!), preferenceBudget: preferenceBudget);
            placements += placement.PlacementProbes;
            expanded += placement.ExpandedCells;
            landings += placement.LandingProbes;
            if (placement.Status == KsProcgenSupportedAccessStatus.Candidate)
            {
                if (accepted == null || placement.PreferenceScore.BetterThan(accepted.Placement!.PreferenceScore)) accepted = new()
                {
                    Status = placement.Status, Composition = composition, Placement = placement, Capabilities = capabilities,
                    VariantProbes = variants, PlacementProbes = placements, ExpandedCells = expanded, LandingProbes = landings,
                };
                if (accepted.Placement!.PreferenceScore.Satisfied) return true;
                if (placement.PreferenceSearchTruncated) terminal = Fail(KsProcgenSupportedAccessStatus.BudgetExceeded, "RoomCorePreferenceBudget");
                return false;
            }
            if (placement.Status is KsProcgenSupportedAccessStatus.InvalidInput or KsProcgenSupportedAccessStatus.UnsupportedContent or KsProcgenSupportedAccessStatus.BudgetExceeded)
                terminal = Fail(placement.Status, placement.Issue!.Code);
            return false;
        }
        Visit(0);
        if (terminal?.Status is KsProcgenSupportedAccessStatus.InvalidInput or KsProcgenSupportedAccessStatus.UnsupportedContent) return terminal;
        return accepted == null ? terminal ?? Fail(KsProcgenSupportedAccessStatus.Rejected, "RoomCoreNoCompletePlacement") : new()
        {
            Status = accepted.Status, Composition = accepted.Composition, Placement = accepted.Placement, Capabilities = accepted.Capabilities,
            VariantProbes = variants, PlacementProbes = placements, ExpandedCells = expanded, LandingProbes = landings,
            PreferenceSearchTruncated = terminal?.Status == KsProcgenSupportedAccessStatus.BudgetExceeded || accepted.Placement!.PreferenceSearchTruncated,
        };
    }
}
