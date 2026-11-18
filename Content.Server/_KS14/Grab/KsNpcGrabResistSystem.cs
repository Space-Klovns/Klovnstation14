using Content.Server.NPC.HTN;
using Content.Shared._KS14.Carrying;
using Content.Shared._KS14.Grab;
using Content.Shared._KS14.Resist;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Grab;

/// <summary>
///     NPCs held in a grip that holds them in place, or carried, struggle on their own, as a player would by pressing
///         resist. The grab and the carry say when a hold starts, which marks the NPC with
///         <see cref="KsNpcResistingComponent"/>; only marked NPCs are updated.
/// </summary>
public sealed partial class KsNpcGrabResistSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private KsGrabSystem _grabSystem = default!;
    [Dependency] private KsResistSystem _resistSystem = default!;

    [SubscribeLocalEvent]
    private void OnGrabStageChanged(Entity<HTNComponent> npc, ref KsGrabStageChangedEvent args)
    {
        // Raised on the grabber as well. A plain pull isn't fought: otherwise nobody could ever get a grip on an NPC.
        if (args.Victim == npc.Owner && args.NewStage.BlocksVictimMovement)
            StartResisting(npc);
    }

    [SubscribeLocalEvent]
    private void OnCarryStarted(Entity<HTNComponent> npc, ref KsCarryStartedEvent args)
    {
        StartResisting(npc);
    }

    private void StartResisting(EntityUid npcUid)
    {
        // Let go and held again within a tick, the marker from the last hold is still there, on its way out: it
        // would be found, kept, and gone by the end of the tick.
        if (TryComp<KsNpcResistingComponent>(npcUid, out var leavingComponent) &&
            leavingComponent.LifeStage > ComponentLifeStage.Running)
        {
            RemComp<KsNpcResistingComponent>(npcUid);
        }

        EnsureComp<KsNpcResistingComponent>(npcUid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        var query = EntityQueryEnumerator<KsNpcResistingComponent>();
        while (query.MoveNext(out var uid, out var resisting))
        {
            if (curTime < resisting.NextAttempt)
                continue;

            // Free again, or no longer an NPC: nothing more to do until the next hold marks them.
            if (!HasComp<HTNComponent>(uid) || !IsHeld(uid))
            {
                RemCompDeferred<KsNpcResistingComponent>(uid);
                continue;
            }

            resisting.NextAttempt = curTime + resisting.Interval;
            _resistSystem.TryResist(uid);
        }
    }

    private bool IsHeld(EntityUid uid)
    {
        return HasComp<KsBeingCarriedComponent>(uid) || _grabSystem.GetHeldStage(uid) is { BlocksVictimMovement: true };
    }
}
