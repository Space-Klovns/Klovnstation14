using Content.Server.Lathe;
using Content.Server.Radio.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._KS14.SPAM;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Radio;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.SPAM;

/// <summary>
///     Lets a lathe ask the salvagers for the materials a recipe is short of, and tells the S.P.A.M. tablets what the
///         silo holds and what has been asked for.
/// </summary>
public sealed partial class KsMaterialRequestSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SharedMaterialStorageSystem _materialStorageSystem = default!;
    [Dependency] private StationSystem _stationSystem = default!;
    [Dependency] private RadioSystem _radioSystem = default!;
    [Dependency] private LatheSystem _latheSystem = default!;

    private static readonly ProtoId<RadioChannelPrototype> RequestChannel = "Supply";

    /// <summary>
    ///     The silo the salvagers fill: one that stands on a station if there is one, or else any silo.
    /// </summary>
    public bool TryGetSilo(out EntityUid siloUid)
    {
        EntityUid? fallbackUid = null;
        var siloEnumerator = EntityQueryEnumerator<OreSiloComponent>();
        while (siloEnumerator.MoveNext(out siloUid, out _))
        {
            if (_stationSystem.GetOwningStation(siloUid) != null)
                return true;

            fallbackUid ??= siloUid;
        }

        siloUid = fallbackUid ?? default;
        return fallbackUid != null;
    }

    /// <summary>
    ///     Fills in what the silo holds and how much more of each material has been asked for. Requests the silo
    ///         has since met are dropped.
    /// </summary>
    /// <param name="alwaysShown">Materials to report even if the silo has never held any and nobody asked.</param>
    public void GetSiloReport(List<KsSPAMSiloMaterial> materials, IReadOnlyList<ProtoId<MaterialPrototype>>? alwaysShown = null)
    {
        if (!TryGetSilo(out var siloUid))
            return;

        var stored = _materialStorageSystem.GetStoredMaterials(siloUid, localOnly: true);
        var requestsComponent = CompOrNull<KsMaterialRequestsComponent>(siloUid);

        foreach (var (materialId, amount) in stored)
        {
            var requested = 0;
            if (requestsComponent != null && requestsComponent.Targets.TryGetValue(materialId, out var target))
            {
                requested = Math.Max(target - amount, 0);
                if (requested == 0)
                    requestsComponent.Targets.Remove(materialId);
            }

            materials.Add(new KsSPAMSiloMaterial(materialId, amount, requested));
        }

        if (requestsComponent != null)
        {
            // Asked for, and the silo has none of it at all.
            foreach (var (materialId, target) in requestsComponent.Targets)
            {
                if (!stored.ContainsKey(materialId))
                    materials.Add(new KsSPAMSiloMaterial(materialId, 0, target));
            }
        }

        if (alwaysShown == null)
            return;

        foreach (var materialId in alwaysShown)
        {
            if (!stored.ContainsKey(materialId) && requestsComponent?.Targets.ContainsKey(materialId) != true)
                materials.Add(new KsSPAMSiloMaterial(materialId, 0, 0));
        }
    }

    /// <summary>
    ///     Asks for what the lathe is short of to make the recipe once.
    /// </summary>
    /// <returns>Whether anything was short.</returns>
    public bool TryRequest(Entity<LatheComponent> lathe, ProtoId<LatheRecipePrototype> recipeId)
    {
        if (!_prototypeManager.TryIndex(recipeId, out var recipePrototype) ||
            !_latheSystem.GetAvailableRecipes(lathe.Owner, lathe.Comp).Contains(recipeId) ||
            !TryGetSilo(out var siloUid))
            return false;

        var requestsComponent = EnsureComp<KsMaterialRequestsComponent>(siloUid);
        var shortfalls = new List<string>();
        foreach (var (materialId, needed) in recipePrototype.Materials)
        {
            var cost = SharedLatheSystem.AdjustMaterial(needed, recipePrototype.ApplyMaterialDiscount, lathe.Comp.MaterialUseMultiplier);
            var shortfall = cost - _materialStorageSystem.GetMaterialAmount(lathe.Owner, materialId);
            if (shortfall <= 0)
                continue;

            // Asking again for what is already asked for says nothing new, and must not fill the radio.
            var target = _materialStorageSystem.GetMaterialAmount(siloUid, materialId, localOnly: true) + shortfall;
            if (target <= requestsComponent.Targets.GetValueOrDefault(materialId))
                continue;

            requestsComponent.Targets[materialId] = target;

            if (_prototypeManager.TryIndex(materialId, out var materialPrototype))
                shortfalls.Add(Loc.GetString("ks-spam-request-entry", ("amount", shortfall), ("material", Loc.GetString(materialPrototype.Name))));
        }

        if (shortfalls.Count == 0)
            return false;

        var message = Loc.GetString("ks-spam-request-announce", ("lathe", lathe.Owner), ("materials", string.Join(", ", shortfalls)));
        _radioSystem.SendRadioMessage(lathe.Owner, message, RequestChannel, lathe.Owner, escapeMarkup: false);
        return true;
    }

    [SubscribeLocalEvent]
    private void OnRequestMessage(EntityUid uid, LatheComponent component, KsLatheRequestMaterialsMessage args)
    {
        TryRequest((uid, component), args.Recipe);
    }
}
