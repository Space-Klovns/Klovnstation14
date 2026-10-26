using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public static partial class KsProcgenOwnedInteriorPlanner
{
    private static bool ValidateUtilityContexts(IPrototypeManager prototypeManager, KsProcgenRequest request, string themeId,
        KsProcgenNormalizedShape shape, KsProcgenThemeCatalog? content, KsProcgenGasPlanningContext? gasContext,
        KsProcgenElectricalPlanningContext? electricalContext, out KsProcgenIssue? issue)
    {
        issue = null;
        if (gasContext == null && electricalContext == null)
            return KsProcgenUtilityContracts.CheckPlanningSupport(request, out issue);
        var utilities = request.Utilities;
        if (gasContext == null && utilities != null)
        {
            // Test the unhandled layer using a detached declaration; do not weaken caller requirements.
            var gasOnly = new KsProcgenUtilityContractSpec
            {
                GasAssignments = utilities.GasAssignments, GasWriteCells = utilities.GasWriteCells,
                GasRestrictions = utilities.GasRestrictions,
            };
            if (KsProcgenUtilityContracts.HasDeclarations(gasOnly))
            { issue = new("AtmospherePlanningUnsupported", "Gas declarations need retained gas topology facts."); return false; }
        }
        if (electricalContext == null && utilities != null && (utilities.Consumers.Count != 0 ||
                utilities.CableWriteCells != null || utilities.DeviceWriteCells != null ||
                utilities.ElectricalRestrictions.RequireOperationalSupply || utilities.ElectricalRestrictions.ForbidBatteryOnly))
        { issue = new("ElectricalPlanningUnsupported", "Electrical requirements need a captured distribution context."); return false; }
        if (gasContext != null && (shape.PreservedCells.Count != 0 || shape.ConstantRegions.Count != 0 || shape.Entrances.Count != 0))
        { issue = new("HostedGasContextCaptureRequired", "Gas planning needs inspected host transfer context."); return false; }
        if (electricalContext == null) return true;
        if (shape.PreservedCells.Count != 0 || shape.ConstantRegions.Count != 0 || shape.Entrances.Count != 0)
        { issue = new("HostedElectricalContextCaptureRequired", "Electrical distribution needs inspected host networks."); return false; }
        if (utilities?.ElectricalRestrictions.RequireOperationalSupply == true || utilities?.ElectricalRestrictions.ForbidBatteryOnly == true)
        { issue = new("ElectricalSupplyObservationRequired", "Hard source restrictions need native supply observation."); return false; }
        if (content == null && !KsProcgenThemeCatalog.TryCapture(prototypeManager, themeId, out content, out issue)) return false;
        if (!KsProcgenElectricalProfiles.TryResolveSelection(prototypeManager, request.ElectricalProfile, request.Electrical,
                content!.Theme.Electrical, out var selected, out issue)) return false;
        // A frozen named request carries an inline copy. Compare declarations with the same
        // provenance label; an authoring reference name alone is not a distribution constraint.
        if (selected == null)
        { issue = new("MismatchedElectricalPlanningContext", "An electrical context needs an effective distribution profile."); return false; }
        if (!KsProcgenElectricalProfiles.TrySnapshot(selected.CopySpec(), out var comparable, out issue,
                sourceProfileId: electricalContext.Profile.SourceProfileId)) return false;
        if (comparable!.SemanticHash != electricalContext.Profile.SemanticHash)
        { issue = new("MismatchedElectricalPlanningContext", "Captured distribution facts must match the effective request profile."); return false; }
        return true;
    }
}
