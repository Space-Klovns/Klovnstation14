using Content.Client._KS14.Damage;
using Content.Client.Damage;
using Content.Shared.Body;

namespace Content.Client._KS14.Klovnmed.LimbDamageVisuals;

public sealed partial class LimbDamageVisualsSystem : EntitySystem
{
    [Dependency] private DamageVisualsSystem _damageVisualsSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LimbDamageVisualsComponent, OrganInsertedIntoEvent>(OnOrganInsertedInto);
        SubscribeLocalEvent<LimbDamageVisualsComponent, OrganRemovedFromEvent>(OnOrganRemovedFrom);

        SubscribeLocalEvent<LimbDamageVisualsComponent, KsGetDamageVisualsEvent>(OnGetDamageVisuals);
    }

    private void OnOrganInsertedInto(Entity<LimbDamageVisualsComponent> entity, ref OrganInsertedIntoEvent args)
        => _damageVisualsSystem.TryForceUpdateLayers(entity.Owner);

    private void OnOrganRemovedFrom(Entity<LimbDamageVisualsComponent> entity, ref OrganRemovedFromEvent args)
        => _damageVisualsSystem.TryForceUpdateLayers(entity.Owner);

    private void OnGetDamageVisuals(Entity<LimbDamageVisualsComponent> entity, ref KsGetDamageVisualsEvent args)
    {
        if (!TryComp<BodyComponent>(entity, out var bodyComponent))
            return;

        foreach (var layerMapKey in args.DamageVisualsComponent.TargetLayerMapKeys)
        {
            if (!entity.Comp.RequiredOrgans.TryGetValue(layerMapKey, out var requiredOrganCategoryId) ||
                bodyComponent.PresentOrganCategories.ContainsKey(requiredOrganCategoryId))
                continue;

            (args.RemovedLayerMapKeys ??= []).Add(layerMapKey);
        }
    }
}
