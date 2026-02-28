using Content.Shared.Mobs.Components;
using Content.Shared.Trigger;
using Content.Shared._KS14.Trigger.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs;
using Content.Server.DeviceLinking.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Trigger.Systems;

public sealed class SignalRattleOnTriggerSystem : EntitySystem
{
    [Dependency] private readonly DeviceLinkSystem _deviceLink = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SignalRattleOnTriggerComponent, ComponentInit>(SignalRattleOnTriggerInit);
        SubscribeLocalEvent<SignalRattleOnTriggerComponent, TriggerEvent>(HandleSignalRattleOnTrigger);
    }
    private void SignalRattleOnTriggerInit(Entity<SignalRattleOnTriggerComponent> ent, ref ComponentInit args)
    {
        _deviceLink.EnsureSourcePorts(ent.Owner, ent.Comp.CritPort);
        _deviceLink.EnsureSourcePorts(ent.Owner, ent.Comp.DeathPort);
    }
    private void HandleSignalRattleOnTrigger(Entity<SignalRattleOnTriggerComponent> ent, ref TriggerEvent args)
    {
        var target = ent.Comp.TargetUser ? args.User : ent.Owner;

        if (target == null)
            return;

        if (!TryComp<MobStateComponent>(target.Value, out var mobstate))
            return;

        args.Handled = true;

        if (mobstate.CurrentState == MobState.Critical)
            _deviceLink.InvokePort(ent.Owner, ent.Comp.CritPort);

        else if (mobstate.CurrentState == MobState.Dead)
            _deviceLink.InvokePort(ent.Owner, ent.Comp.DeathPort);
    }
}
