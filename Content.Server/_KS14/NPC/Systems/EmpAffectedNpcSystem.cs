using Content.Server._KS14.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared.Emp;

namespace Content.Server._KS14.NPC.Systems;

public sealed partial class EmpAffectedNpcSystem : EntitySystem
{
    [Dependency] private NPCSystem _npcSystem = default!;

    [SubscribeLocalEvent]
    private void OnAttemptNpcWork(Entity<EmpAffectedNpcComponent> entity, ref AttemptNpcWorkEvent args)
    {
        if (args.Cancelled ||
            !HasComp<EmpDisabledComponent>(entity))
            return;

        args.Cancelled = true;
    }

    [SubscribeLocalEvent]
    private void OnEmpPulse(Entity<EmpAffectedNpcComponent> entity, ref EmpPulseEvent args)
    {
        args.Affected = true;
        _npcSystem.SleepNPC(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnEmpDisabledRemoved(Entity<EmpAffectedNpcComponent> entity, ref EmpDisabledRemovedEvent args)
    {
        _npcSystem.WakeNPC(entity.Owner);
    }
}
