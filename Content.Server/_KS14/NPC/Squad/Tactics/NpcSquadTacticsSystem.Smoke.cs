using Content.Server._KS14.NPC.Grenades;
using Content.Server._KS14.NPC.Internals;
using Content.Server.Body.Systems;
using Content.Shared._KS14.NPC;
using Content.Shared.Body.Components;
using Content.Shared.Trigger.Components.Effects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Smoking a room before going in (<see cref="NpcSquadAction.SmokeRoom"/>), with smoke marked
///         <see cref="NpcRoomSmokeComponent"/>. What it does to the hostile is read from the grenade:
///     <list type="bullet">
///         <item>Smoke that carries something gasses whatever breathes it. Only offered if the hostile would breathe it, and
///             every member would go on internals first (<see cref="NpcInternalsSystem"/>).</item>
///         <item>Smoke that blocks sight screens a hostile that sees by it: anyone but an NPC that sees through smoke
///             (<see cref="Perception.NpcPerceptionComponent.SeesThroughSmoke"/>). Only offered by a squad that all does, or
///             it would blind itself.</item>
///     </list>
///     Gas is preferred: it blinds wherever the hostile looks, and slows it.
/// </summary>
public sealed partial class NpcSquadTacticsSystem
{
    [Dependency] private InternalsSystem _internalsSystem = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private NpcInternalsSystem _npcInternalsSystem = default!;

    [Dependency] private EntityQuery<BloodstreamComponent> _bloodstreamQuery = default!;
    [Dependency] private EntityQuery<NpcRoomSmokeComponent> _roomSmokeQuery = default!;
    [Dependency] private EntityQuery<SmokeOnTriggerComponent> _smokeOnTriggerQuery = default!;

    /// <summary>
    ///     With the settle on top, over a grenade and under waiting at an open way, so a squad that can soften a room with it
    ///         does, and uses a flashbang first. Whether a grenade also comes first is down to chance: see
    ///         <see cref="GetSofteningCosts"/>.
    /// </summary>
    private const float SmokeCost = 2.5f;

    /// <summary>
    ///     Whether <paramref name="itemUid"/> is smoke worth putting in on <paramref name="targetUid"/>, and if so whether it
    ///         gasses.
    /// </summary>
    /// <param name="squadOnInternals">Whether every member would go on internals: gas is only offered if so.</param>
    /// <param name="squadSeesThroughSmoke">Whether every member sees through smoke: a screen is only offered if so.</param>
    private bool TryGetSmokeUse(EntityUid itemUid, EntityUid targetUid, bool squadOnInternals, bool squadSeesThroughSmoke, out bool gasses)
    {
        gasses = false;

        if (!_roomSmokeQuery.HasComp(itemUid) ||
            !_timerTriggerQuery.HasComp(itemUid) ||
            !_smokeOnTriggerQuery.TryComp(itemUid, out var smokeOnTriggerComponent) ||
            TerminatingOrDeleted(targetUid))
            return false;

        if (squadOnInternals &&
            smokeOnTriggerComponent.Solution.Contents.Count > 0 &&
            _bloodstreamQuery.HasComp(targetUid) &&
            !_internalsSystem.AreInternalsWorking(targetUid))
        {
            gasses = true;
            return true;
        }

        return squadSeesThroughSmoke &&
            !SeesThroughSmoke(targetUid) &&
            _prototypeManager.TryIndex(smokeOnTriggerComponent.SmokePrototype, out var smokePrototype) &&
            smokePrototype.TryGetComponent<OccluderComponent>(out _, EntityManager.ComponentFactory);
    }

    /// <summary>
    ///     What a grenade and smoke cost <paramref name="plan"/>. Which of the two goes first, where it could do either, is down
    ///         to chance (<see cref="NpcSquadTacticsSettings.PlanSmokeOverFragChance"/>), rolled once a plan: the two swap
    ///         costs, both still over a flashbang and under waiting at an open way.
    /// </summary>
    private void GetSofteningCosts(NpcSquadPlan plan, NpcSquadTacticsSettings settings, out float fragCost, out float smokeCost)
    {
        plan.SmokeOverFrag ??= _robustRandom.Prob(Math.Clamp(settings.PlanSmokeOverFragChance, 0f, 1f));

        fragCost = FragCost;
        smokeCost = SmokeCost + (float) settings.PlanSmokeSettle.TotalSeconds;

        if (plan.SmokeOverFrag.Value)
            (fragCost, smokeCost) = (smokeCost, fragCost);
    }

    private bool SeesThroughSmoke(EntityUid uid)
    {
        return _perceptionQuery.TryComp(uid, out var perceptionComponent) && perceptionComponent.SeesThroughSmoke;
    }

    /// <summary>
    ///     Whether every member, those recovering included, sees through smoke.
    /// </summary>
    private bool DoesSquadSeeThroughSmoke()
    {
        foreach (var memberUid in GetEveryMember())
        {
            if (!SeesThroughSmoke(memberUid))
                return false;
        }

        return true;
    }

    /// <summary>
    ///     Whether every member, those recovering included, would go on internals when told to.
    /// </summary>
    private bool CanSquadGoOnInternals()
    {
        foreach (var memberUid in GetEveryMember())
        {
            if (!_npcInternalsSystem.CanGoOnInternals(memberUid))
                return false;
        }

        return true;
    }

    /// <summary>
    ///     Puts every member on internals for <see cref="NpcSquadTacticsSettings.PlanSmokeInternalsTime"/>, for gas about to
    ///         go in.
    /// </summary>
    /// <returns>Whether all of them are on now.</returns>
    private bool PutSquadOnInternals(NpcSquadTacticsSettings settings, TimeSpan now)
    {
        var until = now + settings.PlanSmokeInternalsTime;
        var allOn = true;

        // Not short-circuited: every one of them is kept on, whether or not the one before was.
        foreach (var memberUid in GetEveryMember())
        {
            allOn &= _npcInternalsSystem.KeepOn(memberUid, until);
        }

        return allOn;
    }

    /// <summary>
    ///     Every member, those recovering included: <see cref="_members"/> first, then <see cref="_recovering"/>. Allocates
    ///         nothing.
    /// </summary>
    private EveryMemberEnumerator GetEveryMember()
    {
        return new EveryMemberEnumerator(_members, _recovering);
    }

    private struct EveryMemberEnumerator
    {
        private readonly List<EntityUid> _members;
        private readonly List<EntityUid> _recovering;
        private int _index;

        public EveryMemberEnumerator(List<EntityUid> members, List<EntityUid> recovering)
        {
            _members = members;
            _recovering = recovering;
            _index = 0;
            Current = default;
        }

        public EntityUid Current { get; private set; }

        public EveryMemberEnumerator GetEnumerator()
        {
            return this;
        }

        public bool MoveNext()
        {
            if (_index < _members.Count)
            {
                Current = _members[_index++];
                return true;
            }

            var recoveringIndex = _index - _members.Count;
            if (recoveringIndex >= _recovering.Count)
                return false;

            Current = _recovering[recoveringIndex];
            _index++;
            return true;
        }
    }
}
