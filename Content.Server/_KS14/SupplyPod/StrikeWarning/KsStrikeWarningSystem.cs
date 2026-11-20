using Content.Server._KS14.SupplyPod.LandingZone;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._KS14.SupplyPod.StrikeWarning;
using Content.Shared.Trigger.Components.Effects;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Server._KS14.SupplyPod.StrikeWarning;

/// <summary>
///     Sizes a strike warning to the blast of what is coming down on it.
/// </summary>
public sealed partial class KsStrikeWarningSystem : EntitySystem
{
    [Dependency] private ExplosionSystem _explosionSystem = default!;

    [SubscribeLocalEvent]
    private void OnIndicatorSpawned(Entity<KsStrikeWarningComponent> entity, ref SupplyPodLandingZoneIndicatorSpawnedEvent args)
    {
        if (!TryComp<ExplosionOnTriggerComponent>(args.PodUid, out var explosionComponent)
            || explosionComponent.IntensitySlope <= 0f
            || explosionComponent.TotalIntensity <= 0f)
            return;

        // The radius of a disc of as many tiles as the explosion covers, unobstructed.
        var blastRadius = _explosionSystem.IntensityToRadius(
            explosionComponent.TotalIntensity,
            explosionComponent.IntensitySlope,
            explosionComponent.MaxTileIntensity);

        if (!float.IsFinite(blastRadius))
            return;

        entity.Comp.Radius = MathF.Max(blastRadius, entity.Comp.MinimumRadius);
        Dirty(entity);
    }
}
