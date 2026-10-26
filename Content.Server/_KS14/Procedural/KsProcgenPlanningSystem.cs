using Content.Shared._KS14.Procedural;

namespace Content.Server._KS14.Procedural;

/// <summary>Server entry to the shared owned planner, supplying static component inspection context.</summary>
public sealed partial class KsProcgenPlanningSystem : EntitySystem
{
    public KsProcgenOwnedInteriorResult Propose(KsProcgenRequest request, string themeId,
        KsProcgenGroupRouteContext context, IReadOnlyList<KsProcgenExteriorWindowFact> exteriorFacts,
        KsProcgenOwnedPlanBudgets? budgets = null, KsProcgenThemeCatalog? content = null, KsProcgenThemeCatalog? utilityContent = null)
    {
        if (!KsProcgenGeometry.TryNormalize(request, out _, out var inputIssue))
            return new() { Status = KsProcgenOwnedPlanStatus.InvalidInput, Issue = inputIssue };
        if (content == null && !KsProcgenThemeCatalog.TryCapture(ProtoMan, themeId, out content, out var captureIssue))
            return new() { Status = KsProcgenOwnedPlanStatus.InvalidInput, Issue = captureIssue };
        var utility = request.Utilities;
        if (!KsProcgenElectricalProfiles.TryResolveSelection(ProtoMan, request.ElectricalProfile, request.Electrical,
                content!.Theme.Electrical, out var electrical, out var electricalIssue))
            return new() { Status = KsProcgenOwnedPlanStatus.InvalidInput, Issue = electricalIssue };
        KsProcgenElectricalPlanningContext? electricalContext = null;
        if (!TryResolveElectricalUtilityContent(electrical, utilityContent, out utilityContent, out electricalIssue))
            return new() { Status = KsProcgenOwnedPlanStatus.Rejected, Issue = electricalIssue };
        if (electrical != null && !TryCaptureElectricalContext(content, electrical, out electricalContext, out electricalIssue, utilityContent: utilityContent))
            return new() { Status = KsProcgenOwnedPlanStatus.Rejected, Issue = electricalIssue };
        var restrictions = utility?.GasRestrictions;
        var needsGas = request.Atmosphere != null || request.AtmosphereProfile != null || content!.Theme.Atmosphere != null || utilityContent?.Theme.Atmosphere != null ||
            utility?.GasAssignments.Count > 0 || utility?.GasWriteCells != null || restrictions?.AllowedModes != null ||
            restrictions?.AllowedGases != null || restrictions?.MinimumPressureKPa != null || restrictions?.MaximumPressureKPa != null ||
            restrictions?.MinimumTemperatureKelvin != null || restrictions?.MaximumTemperatureKelvin != null || restrictions?.MinimumObservationTicks != null;
        KsProcgenGasPlanningContext? gasContext = null;
        if (needsGas && !TryCaptureGasContext(content!, out gasContext, out var issue, electrical: electrical, utilityContent: utilityContent))
            return new() { Status = KsProcgenOwnedPlanStatus.Rejected, Issue = issue };
        return KsProcgenOwnedInteriorPlanner.Plan(ProtoMan, request, themeId, context, exteriorFacts,
            budgets: budgets, content: content, componentFactory: Factory, gasContext: gasContext, electricalContext: electricalContext);
    }
}
