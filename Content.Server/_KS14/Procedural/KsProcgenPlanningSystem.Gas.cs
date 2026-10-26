using System.Linq;
using Content.Server.Atmos.Components;
using Content.Shared._KS14.Procedural;
using Content.Shared.Atmos;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenPlanningSystem
{
    [Dependency] private KsProcgenPrototypeManifestSystem _prototypeManifestSystem = default!;

    /// <summary>Capture all candidate initial defaults once, before any configuration chooses content.</summary>
    public bool TryCaptureGasContext(KsProcgenThemeCatalog catalog, out KsProcgenGasPlanningContext? context,
        out KsProcgenIssue? issue, KsProcgenElectricalProfile? electrical = null, KsProcgenThemeCatalog? utilityContent = null)
    {
        context = null;
        var catalogs = utilityContent == null ? new[] { catalog } : new[] { catalog, utilityContent };
        if (!_prototypeManifestSystem.TryCapture(catalogs.SelectMany(item => item.CandidateEntityIds())
                .Concat(electrical == null ? [] : ElectricalPrototypeIds(electrical)),
                catalogs.SelectMany(item => item.CandidateTileIds()), out var manifest, out issue)) return false;
        var facts = new List<KsProcgenGasPrototypeFact>();
        foreach (var root in manifest!.Roots.Where(root => root.Kind == KsProcgenPrototypeBodyKind.Entity))
        {
            if (!_prototypeManifestSystem.TryCreatePrototypeSnapshot(manifest, root.PrototypeId, out var prototype, out issue)) return false;
            prototype!.TryComp<TransformComponent>(out var transform, Factory);
            prototype.TryComp<AirtightComponent>(out var airtight, Factory);
            facts.Add(new(root.PrototypeId, transform?.Anchored ?? false,
                airtight?.AirBlocked == true ? (AtmosDirection) airtight.InitialAirBlockedDirection : AtmosDirection.Invalid,
                airtight?.AirBlocked == true && airtight.NoAirWhenFullyAirBlocked, transform?.NoLocalRotation ?? false));
        }
        return KsProcgenGasPlanningContext.TryCreate(facts, manifest.ContentHash, out context, out issue);
    }
}
