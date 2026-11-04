using Content.Server._KS14.NPC.Hands;
using Content.Server.Hands.Systems;
using Content.Shared._Trauma.Hands;
using Content.Shared.Trigger.Systems;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Grenades;

/// <summary>
///     Has an NPC throw a grenade. It all happens at once rather than as a chain of tasks, and the fuse is only lit once the
///         grenade has left its hand, so nothing can leave a live grenade in its hand or back in its belt.
/// </summary>
public sealed partial class NpcGrenadeSystem : EntitySystem
{
    [Dependency] private HandsSystem _handsSystem = default!;
    [Dependency] private NpcHandsSystem _npcHandsSystem = default!;
    [Dependency] private PredictedHandsSystem _predictedHandsSystem = default!;
    [Dependency] private TriggerSystem _triggerSystem = default!;

    /// <summary>
    ///     Takes <paramref name="grenadeUid"/> out, throws it at <paramref name="coordinates"/>, lights it, and gets the
    ///         weapon back in hand. A throw that does not happen leaves the grenade unlit, back where it was.
    /// </summary>
    public bool TryThrow(EntityUid uid, EntityUid grenadeUid, EntityCoordinates coordinates)
    {
        if (!_npcHandsSystem.TryTakeOut(uid, grenadeUid, out var takenOut))
            return false;

        var thrown = _predictedHandsSystem.ThrowHeldItem(uid, coordinates) && !_handsSystem.IsHolding(uid, grenadeUid);

        if (thrown)
            _triggerSystem.ActivateTimerTrigger(grenadeUid, uid);

        _npcHandsSystem.PutBack(uid, takenOut);
        return thrown;
    }
}
