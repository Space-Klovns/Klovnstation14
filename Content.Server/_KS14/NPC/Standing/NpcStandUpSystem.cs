using Content.Server._KS14.NPC.PlayDead;
using Content.Server._KS14.NPC.Utility;
using Content.Shared.Movement.Components;
using Content.Shared.NPC;
using Content.Shared.Stunnable;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Standing;

/// <summary>
///     Gets every awake NPC back up off the floor once nothing keeps it there, the way
///         a player does: it asks to stand, and the game's own stand-up (its do-after, the room check) does the rest.
///         A reflex here, not a root branch, so that it needs nothing from the root and interrupts nothing: crawling out
///         of a fire, say, it gets up on the way.
///     It leaves alone a knockdown the game is already getting it up from, one with time left, one something keeps it
///         down through (anything cancelling <see cref="StandUpAttemptEvent"/>: soft crit, sleep, a knockdown status
///         effect), a dive in the air, and lying there on purpose (playing dead).
/// </summary>
public sealed partial class NpcStandUpSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private NpcPlayDeadSystem _npcPlayDeadSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;

    [Dependency] private EntityQuery<ActiveLeaperComponent> _leaperQuery = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);

    private TimeSpan _nextUpdate;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!NpcThrottle.TryConsume(ref _nextUpdate, _gameTiming.CurTime, UpdateInterval))
            return;

        // The knocked down first: far fewer than the awake NPCs.
        var enumerator = EntityQueryEnumerator<KnockedDownComponent, ActiveNPCComponent>();
        while (enumerator.MoveNext(out var uid, out var knockedDownComponent, out _))
        {
            // Already on its way up, or will be the moment it can: the game's own.
            if (knockedDownComponent.AutoStand || knockedDownComponent.DoAfterId.HasValue)
                continue;

            if (_leaperQuery.HasComp(uid) || _npcPlayDeadSystem.IsPlayingDead(uid))
                continue;

            // Time left on the knockdown, unable to move, or something keeping it down.
            if (!_stunSystem.CanStand((uid, knockedDownComponent)))
                continue;

            // What the stand-up key does. With no room to stand, the game turns AutoStand off again once its do-after is
            //      done, and this asks again: a player pressing the key until there is room.
            _stunSystem.SetAutoStand((uid, knockedDownComponent), autoStand: true);
            _stunSystem.TryStanding((uid, knockedDownComponent));
        }
    }
}
