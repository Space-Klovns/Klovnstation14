using Content.Shared._KS14.ScanDiscoverable.Base;
using Content.Shared.Trigger.Systems;

namespace Content.Shared._KS14.ScanDiscoverable.Trigger;

public sealed class KsScanTriggerOnDiscoveredSystem : EntitySystem
{
    [Dependency] private readonly TriggerSystem _triggerSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsScanTriggerOnDiscoveredComponent, KsAfterScanDiscoveringEvent>(OnDiscover);
    }

    private void OnDiscover(Entity<KsScanTriggerOnDiscoveredComponent> entity, ref KsAfterScanDiscoveringEvent args)
    {
        if (entity.Owner != args.InteractUsingEvent.Target)
            return;

        _triggerSystem.Trigger(entity.Owner, args.InteractUsingEvent.Used, entity.Comp.KeyOut);
    }
}
