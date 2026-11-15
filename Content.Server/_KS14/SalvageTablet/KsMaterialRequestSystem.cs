using Content.Server.Lathe;
using Content.Server.Radio.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._KS14.SalvageTablet;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Radio;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.SalvageTablet;

/// <summary>
///     Lets a lathe ask the salvagers for the materials a recipe is short of, and tells the salvage tablets what the
///         silo holds and what has been asked for.
/// </summary>
public sealed partial class KsMaterialRequestSystem : EntitySystem
{
    [Dependency] private SharedMaterialStorageSystem _materialStorageSystem = default!;
    [Dependency] private StationSystem _stationSystem = default!;
    [Dependency] private RadioSystem _radioSystem = default!;
    [Dependency] private LatheSystem _latheSystem = default!;
    [Dependency] private EntityQuery<KsMaterialRequestsComponent> _requestsQuery = default!;

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
    ///     Forgets the requests the silo has met since they were made, so a request ends once it is filled rather than
    ///         coming back the next time the silo dips below it.
    /// </summary>
    public void PruneMetRequests()
    {
        if (!TryGetSilo(out var siloUid) || !_requestsQuery.TryComp(siloUid, out var requestsComponent))
            return;

        PruneMetRequests((siloUid, requestsComponent));
    }

    private void PruneMetRequests(Entity<KsMaterialRequestsComponent> siloEntity)
    {
        // A Dictionary may have entries removed while it is being enumerated.
        var targets = siloEntity.Comp.Targets;
        foreach (var (materialId, target) in targets)
        {
            if (_materialStorageSystem.GetMaterialAmount(siloEntity.Owner, materialId, localOnly: true) >= target)
                targets.Remove(materialId);
        }
    }

    /// <summary>
    ///     Fills in what the silo holds and how much more of each material has been asked for. Only reads: met
    ///         requests are forgotten by <see cref="PruneMetRequests()"/>.
    /// </summary>
    /// <param name="alwaysShown">Materials to report even if the silo has never held any and nobody asked.</param>
    public void GetSiloReport(List<KsSalvageTabletSiloMaterial> materials, IReadOnlyList<ProtoId<MaterialPrototype>>? alwaysShown = null)
    {
        if (!TryGetSilo(out var siloUid))
            return;

        var stored = _materialStorageSystem.GetStoredMaterials(siloUid, localOnly: true);
        var requestsComponent = _requestsQuery.CompOrNull(siloUid);

        foreach (var (materialId, amount) in stored)
        {
            var requested = 0;
            if (requestsComponent != null && requestsComponent.Targets.TryGetValue(materialId, out var target))
                requested = Math.Max(target - amount, 0);

            materials.Add(new KsSalvageTabletSiloMaterial(materialId, amount, requested));
        }

        if (requestsComponent != null)
        {
            // Asked for, and the silo has none of it at all.
            foreach (var (materialId, target) in requestsComponent.Targets)
            {
                if (!stored.ContainsKey(materialId))
                    materials.Add(new KsSalvageTabletSiloMaterial(materialId, 0, target));
            }
        }

        if (alwaysShown == null)
            return;

        foreach (var materialId in alwaysShown)
        {
            if (!stored.ContainsKey(materialId) && requestsComponent?.Targets.ContainsKey(materialId) != true)
                materials.Add(new KsSalvageTabletSiloMaterial(materialId, 0, 0));
        }
    }

    /// <summary>
    ///     Asks for what the lathe is short of to make the recipe once.
    /// </summary>
    /// <returns>Whether anything was short.</returns>
    public bool TryRequest(Entity<LatheComponent> latheEntity, ProtoId<LatheRecipePrototype> recipeId)
    {
        if (!ProtoMan.TryIndex(recipeId, out var recipePrototype) ||
            !_latheSystem.GetAvailableRecipes(latheEntity.Owner, latheEntity.Comp).Contains(recipeId) ||
            !TryGetSilo(out var siloUid))
            return false;

        Entity<KsMaterialRequestsComponent> siloEntity = (siloUid, EnsureComp<KsMaterialRequestsComponent>(siloUid));
        PruneMetRequests(siloEntity);

        var targets = siloEntity.Comp.Targets;
        var shortfalls = new List<string>();
        foreach (var (materialId, needed) in recipePrototype.Materials)
        {
            var cost = SharedLatheSystem.AdjustMaterial(needed, recipePrototype.ApplyMaterialDiscount, latheEntity.Comp.MaterialUseMultiplier);
            var shortfall = cost - _materialStorageSystem.GetMaterialAmount(latheEntity.Owner, materialId);
            if (shortfall <= 0)
                continue;

            // Asking again for what is already asked for says nothing new, and must not fill the radio.
            var target = _materialStorageSystem.GetMaterialAmount(siloUid, materialId, localOnly: true) + shortfall;
            if (target <= targets.GetValueOrDefault(materialId))
                continue;

            targets[materialId] = target;

            if (ProtoMan.TryIndex(materialId, out var materialPrototype))
                shortfalls.Add(Loc.GetString("ks-salvage-tablet-request-entry", ("amount", shortfall), ("material", Loc.GetString(materialPrototype.Name))));
        }

        if (shortfalls.Count == 0)
            return false;

        // Escaped: the lathe's name can carry player text, a hand labeler's for one.
        var message = Loc.GetString("ks-salvage-tablet-request-announce", ("lathe", latheEntity.Owner), ("materials", string.Join(", ", shortfalls)));
        _radioSystem.SendRadioMessage(latheEntity.Owner, message, RequestChannel, latheEntity.Owner);
        return true;
    }

    // BUI messages are by-value class events, which have no Entity<T> handler, so this one takes the uid and component.
    [SubscribeLocalEvent]
    private void OnRequestMessage(EntityUid uid, LatheComponent component, KsLatheRequestMaterialsMessage args)
    {
        TryRequest((uid, component), args.Recipe);
    }
}
