using Content.Server._KS14.NPC.Perception;
using Content.Shared._KS14.NPC;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Softening up a room on a hunt (<see cref="NpcHuntPhase.Soften"/>): stacked up on the room a lost hostile is believed to
///         be in, one member puts a flashbang, a grenade or smoke in, and the rest go in behind it, as a squad plan would.
///     <para>
///         Everything here goes by what the squad last saw of the hostile (<see cref="NpcContactTraits"/>, recorded as each
///             member lost sight of it) and where it believes it is now, never by the hostile itself: it is out of sight. The
///             throw is a squad plan's own (<see cref="RunThrow"/>), on a plan nobody else sees, as a blast is.
///     </para>
/// </summary>
public sealed partial class NpcSquadTacticsSystem
{
    /// <summary>
    ///     The longest softening can take before they go in anyway: the throw, and the longest wait after it.
    /// </summary>
    /// <remarks>
    ///     A property, not a field: it is made of fields in another file of this class, and the order fields across files are
    ///         set in is not to be relied on.
    /// </remarks>
    public static TimeSpan SoftenTimeLimit => ThrowTimeout + FragWaitLimit + TimeSpan.FromSeconds(2);

    /// <summary>
    ///     The squad's freshest belief about <paramref name="targetUid"/>: where whichever member saw it last now believes it
    ///         to be, and what the last of them to see it made of it.
    /// </summary>
    /// <remarks>
    ///     Of the group being updated (<see cref="GetEveryMember"/>): only means anything inside that group's update.
    /// </remarks>
    /// <returns>Whether any member knows anything of it.</returns>
    public bool TryGetSquadBelief(EntityUid targetUid, out NpcContactTraits traits, out EntityCoordinates believedCoordinates)
    {
        traits = NpcContactTraits.None;
        believedCoordinates = default;

        var found = false;
        var latestSeen = TimeSpan.MinValue;
        var latestTraitsSeen = TimeSpan.MinValue;
        EntityUid? latestObserverUid = null;

        foreach (var memberUid in GetEveryMember())
        {
            if (!_npcPerceptionSystem.TryGetContact(memberUid, targetUid, out var contact))
                continue;

            if (contact.LastSeen > latestSeen)
            {
                latestSeen = contact.LastSeen;
                latestObserverUid = memberUid;
                believedCoordinates = contact.LastKnownCoordinates;
                found = true;
            }

            // A callout carries where, not what: what it was like comes from whoever last lost sight of it themselves.
            if ((contact.Traits & NpcContactTraits.Seen) != 0x0 && contact.LastSeen > latestTraitsSeen)
            {
                latestTraitsSeen = contact.LastSeen;
                traits = contact.Traits;
            }
        }

        if (latestObserverUid is { } observerUid &&
            _npcPerceptionSystem.TryGetPredictedCoordinates(observerUid, targetUid, out var predictedCoordinates))
            believedCoordinates = predictedCoordinates;

        return found;
    }

    /// <summary>
    ///     Whether <paramref name="memberUid"/>'s squad is holding its grenades for a hunt: while it stacks up, forces a door or
    ///         softens the room, a throw of a member's own would land before the squad is ready, or on top of the one it is
    ///         putting in. Going in, it throws as it likes, unless something has already gone in this hunt. One on its own
    ///         hunts as a squad of one, and holds its throws for its own hunt the same.
    /// </summary>
    public bool IsHoldingThrows(EntityUid memberUid)
    {
        if (GetHunt(_npcSquadSystem.GetSquadOrSelf(memberUid)) is not { } hunt)
            return false;

        return hunt.Phase is NpcHuntPhase.Stage or NpcHuntPhase.Breach or NpcHuntPhase.Soften ||
            hunt.Phase == NpcHuntPhase.Entry && hunt.Softened;
    }

    /// <summary>
    ///     What a hunt's softening goes by: the squad's belief, or nothing for a hostile nobody knows anything of.
    /// </summary>
    private NpcSofteningTarget GetBelievedSofteningTarget(NpcHunt hunt)
    {
        return hunt.TargetUid is { } targetUid && TryGetSquadBelief(targetUid, out var traits, out var believedCoordinates)
            ? new NpcSofteningTarget(believedCoordinates, traits)
            : default;
    }

    /// <summary>
    ///     Starts softening the room, if it is worth it: a squad that plans, stacked up on the room it believes a hostile is in,
    ///         a way to throw something through, something to throw, and a hostile that was last seen with a gun. What goes in
    ///         is what a plan would pick, at the same costs. Only once a hunt.
    /// </summary>
    /// <returns>Whether it started.</returns>
    private bool TryStartSoften(NpcHunt hunt, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (!settings.Planning ||
            !settings.HuntSoftening ||
            hunt.SoftenTried ||
            hunt.TargetUid is not { } targetUid ||
            hunt.StagedMembers.Count == 0)
            return false;

        hunt.SoftenTried = true;

        if (!TryGetSquadBelief(targetUid, out var traits, out var believedCoordinates) ||
            (traits & NpcContactTraits.Armed) == 0x0 ||
            !IsBelievedInRoom(hunt, believedCoordinates) ||
            (GetWayInFacts(hunt) & NpcSquadFact.ThrowLine) == 0x0)
            return false;

        var plan = new NpcSquadPlan
        {
            TargetUid = targetUid,
            StartedAt = now,
            StepStartedAt = now,
            State = NpcSquadPlanState.Running,
            Hunt = hunt,
            MemberCount = _members.Count,
            Reason = "softening the room",
        };

        hunt.Soften = plan;
        FindThrowers(plan, settings, new NpcSofteningTarget(believedCoordinates, traits));

        if (PickSoftener(plan, settings) is not { } action)
        {
            hunt.Soften = null;
            return false;
        }

        hunt.SoftenAction = action;
        hunt.SoftenGaveUp = false;
        SetPhase(hunt, NpcHuntPhase.Soften, now);
        return true;
    }

    /// <summary>
    ///     The cheapest thing to put in, as a plan would cost it. Never anything dearer than walking in on a gun would be:
    ///         <see cref="TryStartSoften"/> only gets here for a hostile last seen with one.
    /// </summary>
    private NpcSquadAction? PickSoftener(NpcSquadPlan plan, NpcSquadTacticsSettings settings)
    {
        GetSofteningCosts(plan, settings, out var fragCost, out var smokeCost);

        NpcSquadAction? best = null;
        var bestCost = settings.PlanRushRisk;

        if (plan.FlashThrow != null && FlashbangCost < bestCost)
            (best, bestCost) = (NpcSquadAction.Flashbang, FlashbangCost);

        if (plan.FragThrow != null && fragCost < bestCost)
            (best, bestCost) = (NpcSquadAction.FragRoom, fragCost);

        if (plan.SmokeThrow != null && smokeCost < bestCost)
            best = NpcSquadAction.SmokeRoom;

        return best;
    }

    private bool IsBelievedInRoom(NpcHunt hunt, EntityCoordinates believedCoordinates)
    {
        if (!_npcGridSystem.TryGetGrid(hunt.GridUid, out var grid) || !believedCoordinates.IsValid(EntityManager))
            return false;

        var mapCoordinates = _transformSystem.ToMapCoordinates(believedCoordinates);
        var tile = _mapSystem.TileIndicesFor(grid, _transformSystem.ToCoordinates(grid.Owner, mapCoordinates));
        return hunt.RoomTiles.Contains(tile);
    }

    /// <summary>
    ///     Carries the throw out, with the orders it gives over combat, as a plan's are. Done once it has gone off or come to
    ///         nothing: either way, they go in.
    /// </summary>
    /// <returns>Whether to go on to the next phase in the same update.</returns>
    private bool UpdateSoften(EntityUid issuerUid, NpcHunt hunt, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        var plan = hunt.Soften!;

        bool done;
        using (GivePlanOrders())
        {
            done = RunThrow(issuerUid, plan, hunt.SoftenAction, settings, now);
        }

        if (!done && !hunt.SoftenGaveUp && now - hunt.PhaseStartedAt < SoftenTimeLimit)
            return false;

        hunt.Softened |= plan.ThrownAt != null;
        hunt.Soften = null;
        SetPhase(hunt, NpcHuntPhase.Entry, now);
        return true;
    }
}
