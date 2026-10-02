using Content.Shared._KS14.NPC;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Getting back together. A member that has strayed from its leader - left behind covering a door, sent to the
///         far end of a search - and has nothing going on goes back to it, so the squad moves on as a squad. In a
///         fight or out of one: what matters is that nothing is happening right now. A member holding its spot in the
///         squad's cover of the leader's room is with the squad wherever that spot is, and is left there.
/// </summary>
public sealed partial class NpcSquadTacticsSystem
{
    /// <summary>
    ///     How far <paramref name="memberUid"/> strays before regrouping: the usual distance when it is calm, closing in
    ///         on <see cref="NpcSquadTacticsSettings.CautiousRegroupDistance"/> as its caution rises.
    /// </summary>
    private float GetRegroupDistance(EntityUid memberUid, NpcSquadTacticsSettings settings)
    {
        if (settings.CautionMeter is not { } cautionMeter)
            return settings.RegroupDistance;

        var caution = Math.Clamp(_npcMeterSystem.GetFraction(memberUid, cautionMeter), 0f, 1f);
        return settings.RegroupDistance + (settings.CautiousRegroupDistance - settings.RegroupDistance) * caution;
    }

    private void UpdateRegroup(EntityUid issuerUid, EntityUid? leaderUid, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        // On its own, or with somewhere to be: nobody to regroup on, or no time for it.
        if (leaderUid is not { } leader || _npcSquadCoverSystem.HasThreatObjective(leader))
        {
            ClearOrders(issuerUid, _members);
            return;
        }

        var leaderMapCoordinates = _transformSystem.GetMapCoordinates(leader);
        var leaderCoordinates = new EntityCoordinates(leader, default);

        foreach (var memberUid in _members)
        {
            var memberMapCoordinates = _transformSystem.GetMapCoordinates(memberUid);
            var regrouping = HasOrder(memberUid, NpcOrderKind.Regroup);

            if (memberUid == leader ||
                memberMapCoordinates.MapId != leaderMapCoordinates.MapId ||
                _npcPerceptionSystem.HasRecentContact(memberUid, settings.RegroupQuietTime))
            {
                ClearOrder(memberUid, issuerUid);
                continue;
            }

            // A member with a spot in the squad's cover of the leader's room is already with the squad, however far
            //      that spot is from the leader. Regrouping it would walk it in to the leader, and its hold straight back
            //      out to the spot, round and round.
            if (_npcSquadCoverSystem.TryGetAssignment(memberUid, out _))
            {
                ClearOrder(memberUid, issuerUid);
                continue;
            }

            // Far enough to start, and once started, all the way back: no stopping and starting at the edge of it.
            var distance = (memberMapCoordinates.Position - leaderMapCoordinates.Position).Length();
            if (distance > GetRegroupDistance(memberUid, settings) || regrouping && distance > settings.RegroupRange + OrderMoveTolerance)
            {
                // Relative to the leader itself, so it follows the leader if the leader moves.
                SetOrder(memberUid,
                    issuerUid,
                    NpcOrderKind.Regroup,
                    leaderCoordinates,
                    Angle.Zero, // not used: a leader turning about is not a new order
                    settings.RegroupRange,
                    null,
                    now);
                continue;
            }

            ClearOrder(memberUid, issuerUid);
        }
    }
}
