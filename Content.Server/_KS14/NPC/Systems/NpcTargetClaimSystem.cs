using Content.Server._KS14.NPC.Components;
using Content.Server._KS14.NPC.Squad;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Systems;

/// <summary>
///     One to a target, for jobs that only take one: finishing off a hostile, seeing to a squadmate that is down. A claim is
///         kept on the NPC that made it, and checked against its end time when read, so nothing ticks it. HTN claims with
///         <c>ClaimTargetOperator</c>, and passes over what is claimed with <c>TargetUnclaimedCon</c>.
/// </summary>
public sealed partial class NpcTargetClaimSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;

    [Dependency] private EntityQuery<NpcTargetClaimComponent> _targetClaimQuery = default!;

    private readonly HashSet<EntityUid> _squadmates = new();

    /// <summary>
    ///     Claims <paramref name="targetUid"/> for <paramref name="uid"/> for <paramref name="duration"/>, in place of
    ///         whatever it had claimed before.
    /// </summary>
    public void Claim(EntityUid uid, EntityUid targetUid, TimeSpan duration)
    {
        var targetClaimComponent = EnsureComp<NpcTargetClaimComponent>(uid);
        targetClaimComponent.TargetUid = targetUid;
        targetClaimComponent.EndTime = _gameTiming.CurTime + duration;
    }

    /// <summary>
    ///     Whether anyone in <paramref name="uid"/>'s squad but itself has <paramref name="targetUid"/> claimed.
    /// </summary>
    public bool IsClaimedBySquadmate(EntityUid uid, EntityUid targetUid)
    {
        _squadmates.Clear();
        _npcSquadSystem.GetSquadmates(uid, _squadmates);

        var now = _gameTiming.CurTime;

        foreach (var squadmateUid in _squadmates)
        {
            if (_targetClaimQuery.TryComp(squadmateUid, out var targetClaimComponent) &&
                targetClaimComponent.TargetUid == targetUid &&
                now < targetClaimComponent.EndTime)
                return true;
        }

        return false;
    }
}
