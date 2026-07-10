using Content.Shared.Trigger.Systems;

namespace Content.Shared._KS14.SupplyPod.Trigger;

public sealed partial class TriggerOnSupplyPodLandedSystem : EntitySystem
{
    [Dependency] private TriggerSystem _triggerSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TriggerOnSupplyPodLandedComponent, SupplyPodLandedEvent>(OnSupplyPodLanded);
    }

    private void OnSupplyPodLanded(Entity<TriggerOnSupplyPodLandedComponent> entity, ref SupplyPodLandedEvent args)
    {
        _triggerSystem.Trigger(entity.Owner, key: entity.Comp.KeyOut, predicted: true);
    }
}
