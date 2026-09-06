using Content.Client._KS14.Damage;
using Content.Client.Damage;
using Content.Shared.Body;

namespace Content.Client._KS14.Klovnmed.LimbDamageVisuals;

/// <summary>
///     Hides damage overlay layers whose limb is not attached to the body anymore.
/// </summary>
public sealed partial class LimbDamageVisualsSystem : EntitySystem
{
    [Dependency] private DamageVisualsSystem _damageVisualsSystem = default!;
    [Dependency] private EntityQuery<BodyComponent> _bodyQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LimbDamageVisualsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<LimbDamageVisualsComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<LimbDamageVisualsComponent, OrganInsertedIntoEvent>(OnOrganInsertedInto);
        SubscribeLocalEvent<LimbDamageVisualsComponent, OrganRemovedFromEvent>(OnOrganRemovedFrom);

        SubscribeLocalEvent<LimbDamageVisualsComponent, KsGetDamageVisualsEvent>(OnGetDamageVisuals);
    }

    private void OnStartup(Entity<LimbDamageVisualsComponent> entity, ref ComponentStartup args)
        => _damageVisualsSystem.TryForceUpdateLayers(entity.Owner);

    /// <remarks>
    ///     <see cref="OnGetDamageVisuals"/> ignores a component that is shutting down, so this shows
    ///     every layer we were hiding again.
    /// </remarks>
    private void OnShutdown(Entity<LimbDamageVisualsComponent> entity, ref ComponentShutdown args)
        => _damageVisualsSystem.TryForceUpdateLayers(entity.Owner);

    private void OnOrganInsertedInto(Entity<LimbDamageVisualsComponent> entity, ref OrganInsertedIntoEvent args)
        => _damageVisualsSystem.TryForceUpdateLayers(entity.Owner);

    private void OnOrganRemovedFrom(Entity<LimbDamageVisualsComponent> entity, ref OrganRemovedFromEvent args)
        => _damageVisualsSystem.TryForceUpdateLayers(entity.Owner);

    private void OnGetDamageVisuals(Entity<LimbDamageVisualsComponent> entity, ref KsGetDamageVisualsEvent args)
    {
        // Nothing should stay hidden on our behalf once we are going away.
        if (entity.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        if (entity.Comp.RequiredOrgans.Count == 0)
            return;

        // No body at all means no organs at all, so everything we care about is missing.
        _bodyQuery.TryComp(entity, out var bodyComponent);

        foreach (var layerMapKey in args.DamageVisualsComponent.TargetLayerMapKeys)
        {
            if (!entity.Comp.RequiredOrgans.TryGetValue(layerMapKey, out var requiredOrganCategoryId) ||
                (bodyComponent?.PresentOrganCategories.ContainsKey(requiredOrganCategoryId) ?? false))
                continue;

            (args.RemovedLayerMapKeys ??= []).Add(layerMapKey);
        }
    }
}
