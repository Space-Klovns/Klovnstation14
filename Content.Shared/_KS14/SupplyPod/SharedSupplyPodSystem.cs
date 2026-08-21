namespace Content.Shared._KS14.SupplyPod;

public abstract partial class SharedSupplyPodSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ActiveSupplyPodComponent, ComponentShutdown>(OnActiveShutdown);
    }

    private void OnActiveShutdown(Entity<ActiveSupplyPodComponent> entity, ref ComponentShutdown args)
    {
        // ComponentShutdown also fires while the pod itself is being deleted. In that case,
        // spawning the landing payload would attach it to an entity that is terminating.
        if (Comp<MetaDataComponent>(entity).EntityLifeStage >= EntityLifeStage.Terminating)
            return;

        var ev = new SupplyPodLandedEvent();
        RaiseLocalEvent(entity, ev);
    }
}

/// <summary>
///     Raised by-value on a supply pod when it lands.
/// </summary>
public record struct SupplyPodLandedEvent;
