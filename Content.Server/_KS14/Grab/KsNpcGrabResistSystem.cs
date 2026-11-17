using Content.Server.NPC.HTN;
using Content.Shared._KS14.Carrying;
using Content.Shared._KS14.Grab;
using Content.Shared._KS14.Resist;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Grab;

/// <summary>
///     NPCs held in a grip that holds them in place, or carried, struggle on their own, as a player would by pressing resist. How often an attempt
///         lands is up to the grab's own resist cooldown and the carry's escape do-after.
/// </summary>
public sealed partial class KsNpcGrabResistSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private KsGrabSystem _grabSystem = default!;
    [Dependency] private KsResistSystem _resistSystem = default!;

    /// <summary>
    ///     How often each held NPC is checked. Resist cooldowns are longer, so this only bounds the reaction time.
    /// </summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

    private TimeSpan _nextCheck;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_gameTiming.CurTime < _nextCheck)
            return;

        _nextCheck = _gameTiming.CurTime + CheckInterval;

        var query = EntityQueryEnumerator<HTNComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            // A plain pull isn't fought: otherwise nobody could ever get a grip on an NPC.
            if (HasComp<KsBeingCarriedComponent>(uid) || _grabSystem.GetHeldStage(uid) is { BlocksVictimMovement: true })
                _resistSystem.TryResist(uid);
        }
    }
}
