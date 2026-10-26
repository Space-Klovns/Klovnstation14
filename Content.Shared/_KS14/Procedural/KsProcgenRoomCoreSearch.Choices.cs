using System.Linq;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

/// <summary>Exactly one complete selection from this ordered alternative set must survive.</summary>
public sealed record KsProcgenRoomCoreChoice(IReadOnlyList<KsProcgenRoomCoreSelection> Options);

public static partial class KsProcgenRoomCoreSearch
{
    /// <summary>Backtracks complete core choices and poses; all failed branches share one allowance.</summary>
    public static KsProcgenRoomCoreSearchResult SearchChoices(IPrototypeManager prototypeManager, IComponentFactory componentFactory,
        string roomId, IReadOnlyList<KsProcgenRoomCoreChoice> choices, KsProcgenRoomAccessMask room, int seed,
        int maximumChoiceProbes = 4096, int maximumVariantProbes = 64, int maximumPlacementProbes = 4096,
        int maximumExpandedCells = 4096, int maximumLandingProbes = 4096, bool requireConnectedCleanFloor = false,
        KsProcgenRoomPreferencePolicy? preferences = null, KsProcgenPreferenceBudget? preferenceBudget = null)
    {
        var choiceProbes = 0; var variants = 0; var placements = 0; var expanded = 0; var landings = 0;
        preferenceBudget ??= new();
        KsProcgenRoomCoreSearchResult Fail(KsProcgenSupportedAccessStatus status, string code) => new()
        {
            Status = status, Issue = new(code, "Complete room core choices could not produce a supported placement."),
            ChoiceProbes = choiceProbes, VariantProbes = variants, PlacementProbes = placements,
            ExpandedCells = expanded, LandingProbes = landings,
        };
        if (prototypeManager == null || componentFactory == null || string.IsNullOrWhiteSpace(roomId) || roomId.Length > 512 ||
            !KsProcgenSupportedAccessPlanner.ValidMask(room) || !preferenceBudget.Valid || preferences is { Valid: false } ||
            choices == null || choices.Count is < 1 or > 16 || maximumChoiceProbes is < 0 or > 65_536 ||
            maximumVariantProbes is < 0 or > 64 || maximumPlacementProbes is < 0 or > 65_536 ||
            maximumExpandedCells is < 0 or > 65_536 || maximumLandingProbes is < 0 or > 65_536 ||
            choices.Any(choice => choice?.Options == null || choice.Options.Count is < 1 or > 64 ||
                choice.Options.Any(option => option?.Core == null || string.IsNullOrWhiteSpace(option.PackId)) ||
                choice.Options.Select(option => (option.Origin, option.PackId)).Distinct().Count() != 1 ||
                choice.Options.Select(option => (option.Origin, option.PackId, option.Core.Id, option.CopyIndex)).Distinct().Count() != choice.Options.Count))
            return Fail(KsProcgenSupportedAccessStatus.InvalidInput, "InvalidRoomCoreChoices");
        var selected = new List<KsProcgenRoomCoreSelection>();
        KsProcgenRoomCoreSearchResult? accepted = null; KsProcgenRoomCoreSearchResult? terminal = null;
        bool Visit(int index)
        {
            if (index < choices.Count)
            {
                foreach (var option in choices[index].Options)
                {
                    if (choiceProbes >= maximumChoiceProbes || !preferenceBudget.ChargeSelection())
                    { terminal = Fail(KsProcgenSupportedAccessStatus.BudgetExceeded, "RoomCoreChoiceProbeBudget"); return false; }
                    choiceProbes++;
                    if (selected.Any(other => other.Origin == option.Origin && other.PackId == option.PackId &&
                            other.Core.Id == option.Core.Id && other.CopyIndex == option.CopyIndex)) continue;
                    selected.Add(option);
                    if (Visit(index + 1)) return true;
                    selected.RemoveAt(selected.Count - 1);
                    if (terminal != null) return false;
                }
                return false;
            }
            var result = Search(prototypeManager, componentFactory, roomId, selected, room, seed,
                maximumVariantProbes: maximumVariantProbes - variants, maximumPlacementProbes: maximumPlacementProbes - placements,
                maximumExpandedCells: maximumExpandedCells - expanded, maximumLandingProbes: maximumLandingProbes - landings,
                requireConnectedCleanFloor: requireConnectedCleanFloor, preferences: preferences, preferenceBudget: preferenceBudget);
            variants += result.VariantProbes; placements += result.PlacementProbes;
            expanded += result.ExpandedCells; landings += result.LandingProbes;
            if (result.Status == KsProcgenSupportedAccessStatus.Candidate)
            {
                if (accepted == null || result.Placement!.PreferenceScore.BetterThan(accepted.Placement!.PreferenceScore))
                    accepted = new() { Status = result.Status, Composition = result.Composition, Placement = result.Placement,
                    Capabilities = result.Capabilities, ChoiceProbes = choiceProbes, VariantProbes = variants,
                    PlacementProbes = placements, ExpandedCells = expanded, LandingProbes = landings,
                    PreferenceSearchTruncated = result.PreferenceSearchTruncated };
                if (accepted.Placement!.PreferenceScore.Satisfied) return true;
                if (result.PreferenceSearchTruncated) terminal = Fail(KsProcgenSupportedAccessStatus.BudgetExceeded, "RoomChoicePreferenceBudget");
                return false;
            }
            if (result.Status is KsProcgenSupportedAccessStatus.InvalidInput or KsProcgenSupportedAccessStatus.UnsupportedContent or KsProcgenSupportedAccessStatus.BudgetExceeded)
                terminal = Fail(result.Status, result.Issue!.Code);
            return false;
        }
        Visit(0);
        if (terminal?.Status is KsProcgenSupportedAccessStatus.InvalidInput or KsProcgenSupportedAccessStatus.UnsupportedContent) return terminal;
        return accepted == null ? terminal ?? Fail(KsProcgenSupportedAccessStatus.Rejected, "RoomCoreNoCompleteChoice") : new()
        {
            Status = accepted.Status, Composition = accepted.Composition, Placement = accepted.Placement, Capabilities = accepted.Capabilities,
            ChoiceProbes = choiceProbes, VariantProbes = variants, PlacementProbes = placements, ExpandedCells = expanded, LandingProbes = landings,
            PreferenceSearchTruncated = accepted.PreferenceSearchTruncated || terminal?.Status == KsProcgenSupportedAccessStatus.BudgetExceeded,
        };
    }
}
