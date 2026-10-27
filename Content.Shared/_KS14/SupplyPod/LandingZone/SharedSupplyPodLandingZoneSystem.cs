namespace Content.Shared._KS14.SupplyPod.LandingZone;

/// <summary>
///     Exists so <see cref="SupplyPodLandingZoneIndicatorComponent"/> can restrict who writes its
///         countdown. The server decides it; the client only reads it.
/// </summary>
public abstract partial class SharedSupplyPodLandingZoneSystem : EntitySystem
{
    /// <summary>
    ///     Starts the countdown on an indicator.
    /// </summary>
    protected void StartCountdown(Entity<SupplyPodLandingZoneIndicatorComponent> entity, TimeSpan startTime, TimeSpan duration)
    {
        entity.Comp.StartTime = startTime;
        entity.Comp.Duration = duration;
        Dirty(entity);
    }
}
