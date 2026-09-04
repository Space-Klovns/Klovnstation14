using Content.Shared.Trigger;
using Content.Server._KS14.Trigger.Components;
using Content.Server.NPC.HTN;

namespace Content.Server._KS14.Trigger.Systems;

public sealed partial class SetHtnEnabledOnTriggerSystem : XOnTriggerSystem<SetHtnEnabledOnTriggerComponent>
{
    [Dependency] private HTNSystem _htnSystem = default!;

    protected override void OnTrigger(Entity<SetHtnEnabledOnTriggerComponent> entity, EntityUid targetUid, ref TriggerEvent args)
    {
        if (!TryComp<HTNComponent>(targetUid, out var htnComponent))
            return;

        _htnSystem.SetHTNEnabled((targetUid, htnComponent), entity.Comp.Enabled);
        args.Handled = true;
    }
}
