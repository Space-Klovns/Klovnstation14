using Content.Shared._KS14.SupplyPod;
using Content.Shared._KS14.SupplyPod.LandingZone;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Server._KS14.SupplyPod.LandingZone;

/// <summary>
///     tgstation's <c>pod_landingzone</c>: exists for as long as the pod is falling, and goes away
///         the moment it lands.
/// </summary>
public sealed partial class SupplyPodLandingZoneSystem : SharedSupplyPodLandingZoneSystem
{
    [Dependency] private IGameTiming _gameTiming = default!;

    [SubscribeLocalEvent]
    private void OnLaunched(Entity<SupplyPodLandingZoneComponent> entity, ref SupplyPodLaunchedEvent args)
    {
        // Nothing is landing while a pod rises. The descent leg raises this again.
        if (args.Ascending)
            return;

        if (entity.Comp.IndicatorEntity is { } oldIndicatorUid)
        {
            if (!TerminatingOrDeleted(oldIndicatorUid))
                QueueDel(oldIndicatorUid);

            entity.Comp.IndicatorEntity = null;
        }

        if (!TryComp<SupplyPodComponent>(entity.Owner, out var supplyPodComponent))
            return;

        // The pod is already standing on its destination; the client only animates it in from the
        // air. Spawning on its coordinates parents the indicator to the grid under it.
        var indicatorUid = Spawn(entity.Comp.IndicatorProto, Transform(entity.Owner).Coordinates);

        if (!TryComp<SupplyPodLandingZoneIndicatorComponent>(indicatorUid, out var indicatorComponent))
        {
            Log.Error($"Supply pod landing zone prototype '{entity.Comp.IndicatorProto}' has no {nameof(SupplyPodLandingZoneIndicatorComponent)}.");
            Del(indicatorUid);
            return;
        }

        StartCountdown((indicatorUid, indicatorComponent), _gameTiming.CurTime, supplyPodComponent.FallDuration);
        entity.Comp.IndicatorEntity = indicatorUid;
    }

    [SubscribeLocalEvent]
    private void OnLanded(Entity<SupplyPodLandingZoneComponent> entity, ref SupplyPodLandedEvent args)
    {
        ReleaseIndicator(entity.Comp);
    }

    /// <summary>
    ///     A pod destroyed before it lands takes its indicator with it. Landing releases it first,
    ///         so this only ever catches the abnormal case.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnShutdown(Entity<SupplyPodLandingZoneComponent> entity, ref ComponentShutdown args)
    {
        ReleaseIndicator(entity.Comp);
    }

    private void ReleaseIndicator(SupplyPodLandingZoneComponent landingZoneComponent)
    {
        if (landingZoneComponent.IndicatorEntity is not { } indicatorUid)
            return;

        landingZoneComponent.IndicatorEntity = null;

        if (!TerminatingOrDeleted(indicatorUid))
            QueueDel(indicatorUid);
    }
}
