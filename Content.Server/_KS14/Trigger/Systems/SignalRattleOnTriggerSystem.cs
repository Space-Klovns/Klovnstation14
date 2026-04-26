using Content.Shared.Mobs.Components;
using Content.Shared.Trigger;
using Content.Shared._KS14.Trigger.Components;
using Content.Shared.Mobs;
using Content.Server.DeviceLinking.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.Shared.Log;

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
        Logger.Info($"beginning signal process");
        var target = ent.Comp.TargetUser ? args.User : ent.Owner;
        Logger.Info($"target: {target}");
        if (target == null)
            return;
        Logger.Info($"got past target check");
        if (!TryComp<MobStateComponent>(target.Value, out var mobstate))
            return;
        Logger.Info($"got past mobstate check");

        args.Handled = true;
        Logger.Info($"{mobstate.CurrentState} current mobstate");

        if (mobstate.CurrentState == MobState.Critical)
            _deviceLink.InvokePort(ent.Owner, ent.Comp.CritPort);

        else if (mobstate.CurrentState == MobState.Dead)
            _deviceLink.InvokePort(ent.Owner, ent.Comp.DeathPort);
        Logger.Info($"got past death and crit port invocation");
    }
}
