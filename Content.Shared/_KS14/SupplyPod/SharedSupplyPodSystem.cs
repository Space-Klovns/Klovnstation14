namespace Content.Shared._KS14.SupplyPod;

public abstract class SharedSupplyPodSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ActiveSupplyPodComponent, ComponentShutdown>(OnActiveShutdown);
    }

    private void OnActiveShutdown(Entity<ActiveSupplyPodComponent> entity, ref ComponentShutdown args)
    {
        var ev = new SupplyPodLandedEvent();
        RaiseLocalEvent(entity, ev);
    }
}

/// <summary>
///     Raised by-value on a supply pod when it lands.
/// </summary>
public record struct SupplyPodLandedEvent;
