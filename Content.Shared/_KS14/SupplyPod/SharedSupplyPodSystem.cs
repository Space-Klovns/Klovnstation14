using Robust.Shared.Physics.Events;

namespace Content.Shared._KS14.SupplyPod;

public abstract partial class SharedSupplyPodSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActiveSupplyPodComponent, ComponentStartup>(OnActiveStartup);
        SubscribeLocalEvent<ActiveSupplyPodComponent, ComponentShutdown>(OnActiveShutdown);
    }

    [SubscribeLocalEvent]
    private void OnPreventCollide(Entity<ActiveSupplyPodComponent> entity, ref PreventCollideEvent args)
    {
        args.Cancelled = true;
    }

    /// <summary>
    ///     How long a pod takes to fall. Set before it starts falling - on a pod spawned uninitialised, as one starts falling at
    ///         map init - for it to take effect.
    /// </summary>
    public void SetFallDuration(Entity<SupplyPodComponent> pod, TimeSpan duration)
    {
        pod.Comp.FallDuration = duration;
    }

    /// <summary>
    ///     How much of the end of its fall a pod is seen falling (<see cref="SupplyPodComponent.DescentDuration"/>); null for
    ///         the whole fall. Set before it starts falling, as with <see cref="SetFallDuration"/>.
    /// </summary>
    public void SetDescentDuration(Entity<SupplyPodComponent> pod, TimeSpan? duration)
    {
        pod.Comp.DescentDuration = duration;
        Dirty(pod);
    }

    /// <summary>
    ///     How long the pod is seen falling for: its <see cref="SupplyPodComponent.DescentDuration"/>, kept within its fall.
    /// </summary>
    public static TimeSpan GetVisibleDescent(SupplyPodComponent supplyPodComponent)
    {
        if (supplyPodComponent.DescentDuration is not { } descent)
            return supplyPodComponent.FallDuration;

        return descent < TimeSpan.Zero ? TimeSpan.Zero : descent > supplyPodComponent.FallDuration ? supplyPodComponent.FallDuration : descent;
    }

    protected virtual void OnActiveStartup(Entity<ActiveSupplyPodComponent> entity, ref ComponentStartup args)
    {
        RaiseLaunched(entity.Owner, entity.Comp.Ascending);
    }

    /// <summary>
    ///     A launched pod turns around mid-air and starts its descent on the same component, so
    ///         component startup does not cover every leg - the server raises the second one itself.
    /// </summary>
    protected void RaiseLaunched(EntityUid podUid, bool ascending)
    {
        var ev = new SupplyPodLaunchedEvent(ascending);
        RaiseLocalEvent(podUid, ev);
    }

    protected virtual void OnActiveShutdown(Entity<ActiveSupplyPodComponent> entity, ref ComponentShutdown args)
    {
        // ComponentShutdown also fires while the pod itself is being deleted. In that case,
        // spawning the landing payload would attach it to an entity that is terminating.
        if (Comp<MetaDataComponent>(entity).EntityLifeStage >= EntityLifeStage.Terminating)
            return;

        // A pod that is still on its way up never landed. The ascent leg hands over to the descent
        // leg on the same component, so this only catches the component being torn off mid-flight.
        if (entity.Comp.Ascending)
            return;

        var ev = new SupplyPodLandedEvent();
        RaiseLocalEvent(entity, ev);
    }
}

/// <summary>
///     Raised by-value on a supply pod when it starts flying, for each leg of its trip.
/// </summary>
/// <param name="Ascending">
///     Whether the pod is rising away from the ground rather than falling towards it.
/// </param>
public record struct SupplyPodLaunchedEvent(bool Ascending);

/// <summary>
///     Raised by-value on a supply pod when it lands.
/// </summary>
public record struct SupplyPodLandedEvent;
