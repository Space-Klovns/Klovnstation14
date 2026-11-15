using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Threading;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Voices;
using Content.Server.NPC.Pathfinding;
using Content.Shared._KS14.NPC;
using Content.Shared.Explosion.Components;
using Content.Shared.Physics;
using Content.Shared.Trigger.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     What a squad does together in a fight, beyond each member fighting its own: going forward on a hostile when it has a
///         moment (<see cref="NpcOrderKind.Push"/>), and sending members round the side of one it is all fighting from the
///         front (<see cref="NpcOrderKind.Flank"/>). Both are orders over combat, carried out firing; the rest fight on.
///     <para>
///         The moment to push is one anyone fighting could see or hear: the hostile has gone quiet after shooting
///             (<see cref="NpcGunfireSystem"/>), or one of the squad's own grenades has just gone off on it.
///     </para>
/// </summary>
public sealed partial class NpcSquadTacticsSystem
{
    [Dependency] private NpcGunfireSystem _npcGunfireSystem = default!;
    [Dependency] private NpcVoiceSystem _npcVoiceSystem = default!;

    [Dependency] private SharedContainerSystem _containerSystem = default!;

    [Dependency] private EntityQuery<NpcSquadFightComponent> _fightQuery = default!;

    /// <summary>
    ///     How near a live grenade has to be to anyone fighting, or to where one is going, for nobody to go forward or round:
    ///         about the reach of a frag, and a step.
    /// </summary>
    private const float GrenadeKeepOff = 4f;

    /// <summary>
    ///     How far round the hostile live grenades are looked for.
    /// </summary>
    private const float GrenadeSearchRange = 16f;

    /// <summary>
    ///     How close to its spot a pusher or flanker has to be for that to count as there.
    /// </summary>
    private const float FightOrderRange = 1f;

    /// <summary>
    ///     The least angle between two pushers' ways in, seen from the hostile: so they come at it from around it, and not all
    ///         to one tile.
    /// </summary>
    private static readonly Angle PushSpread = Angle.FromDegrees(25);

    private static readonly Angle[] StandSpotTurns =
        [Angle.Zero, Angle.FromDegrees(15), Angle.FromDegrees(-15), Angle.FromDegrees(30), Angle.FromDegrees(-30)];
    /// <summary>
    ///     Where round the hostile a flanker may go, from the squad: its sides, and on round behind it, which is
    ///         where a way through maintenance or round a block usually comes out.
    /// </summary>
    private static readonly Angle[] FlankTurns =
    [
        Angle.FromDegrees(90), Angle.FromDegrees(-90), Angle.FromDegrees(120), Angle.FromDegrees(-120), Angle.FromDegrees(150),
        Angle.FromDegrees(-150), Angle.FromDegrees(180),
    ];

    /// <summary>
    ///     The most ways round looked for at once.
    /// </summary>
    private const int MaxFlankProbes = 6;

    /// <summary>
    ///     How long the ways round may take to find before the flank is given up on.
    /// </summary>
    private static readonly TimeSpan FlankProbeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     How long a flank goes on with nobody in the squad in sight of the hostile: the front ducking back into cover for a
    ///         moment, while the flankers are on their way round.
    /// </summary>
    private static readonly TimeSpan FlankSightGrace = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     How near the hostile, in tiles, a way round may not go; and how near the line from a fighter to it, which is the
    ///         squad's own front.
    /// </summary>
    private const float FlankHostileKeepOff = 3f;

    private const float FlankFrontWidth = 1.2f;

    /// <summary>
    ///     How near the flanker its own front is left open, for it to set off from at all.
    /// </summary>
    private const float FlankStartClearance = 1.6f;

    /// <summary>
    ///     How much dearer a way round is for the share of it the hostile can see: one wholly in its sight costs as much as one
    ///         four times as long out of it. In the open every way is seen, and the shortest goes; on a station, a way through
    ///         maintenance beats one across its front. The last few tiles, where it comes out to shoot, do not count.
    /// </summary>
    private const float FlankExposureCost = 3f;

    private const int FlankFinalStretch = 3;

    private const float FlankSightRange = 20f;

    /// <summary>
    ///     A waypoint every few tiles of a way round, and how near one a flanker has to be to be sent on to the next.
    /// </summary>
    private const int FlankWaypointStep = 4;

    /// <summary>
    ///     Further than the order's own range (<see cref="FightOrderRange"/>): tactics update only every half second, so at
    ///         1.5 a flanker usually got to a waypoint and stood there until the next update sent it on. The last waypoint is
    ///         walked all the way to.
    /// </summary>
    private const float FlankWaypointReach = 2.5f;

    /// <summary>
    ///     How long a squad too spread out to flank is left before it is looked at again.
    /// </summary>
    private static readonly TimeSpan FlankRecheckDelay = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan PushCalloutCooldown = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan FlankCalloutCooldown = TimeSpan.FromSeconds(10);

    private readonly List<EntityUid> _fightContacts = new();
    private readonly Dictionary<EntityUid, int> _fightCounts = new();
    private readonly List<EntityUid> _fightMembers = new();
    /// <summary>
    ///     Within one <see cref="StartPush"/>: each pusher's index in <see cref="_fighters"/>, and which way from the hostile
    ///         it goes in from, relative to the way the pushers are from it on the whole.
    /// </summary>
    private readonly List<(int Index, Angle Bearing)> _pushBearings = new();
    private readonly List<Vector2> _orderSpotPositions = new();
    private readonly List<MapCoordinates> _routePoints = new();

    /// <summary>
    ///     The fighters (<see cref="GatherFight"/>), and, by the same index, where each is (<see cref="_fighterCoordinates"/>)
    ///         and, once asked for, whether it is armed and what its shots stop at (<see cref="_fighterArmed"/>,
    ///         <see cref="_fighterShotMasks"/>, filled by <see cref="EnsureFighterTraits"/>).
    ///     <para>
    ///         Only <see cref="GatherFight"/> writes them, once for each group, at the start of that group's
    ///             <c>UpdateGroup</c>; they are rewritten for the next group, so they mean something only inside the same update,
    ///             on the main thread, and only after <see cref="GatherFight"/> has run for that group. Nothing may keep an index
    ///             into them, or a copy, beyond it, and nothing on a worker thread may read them (the path jobs get their own
    ///             data: see <see cref="GetFlankAvoidedTiles"/>). The armed and shot mask lists are empty until
    ///             <see cref="EnsureFighterTraits"/>, which what is about to send someone somewhere (a push, ways round for a
    ///             flank) calls first.
    ///     </para>
    /// </summary>
    private readonly List<EntityUid> _fighters = new();
    private readonly List<MapCoordinates> _fighterCoordinates = new();
    private readonly List<bool> _fighterArmed = new();
    private readonly List<CollisionGroup> _fighterShotMasks = new();
    private bool _fighterTraitsKnown;

    /// <summary>
    ///     The hostile <see cref="GatherFight"/> found most of the group fighting, if it found fighters at all.
    /// </summary>
    private EntityUid _fightHostileUid;
    private bool _hasFighters;

    /// <summary>
    ///     One of <paramref name="memberUid"/>'s grenades has gone off at <paramref name="coordinates"/>: its squad may push on
    ///         a hostile caught by it.
    /// </summary>
    public void NoteBlast(EntityUid memberUid, EntityCoordinates coordinates)
    {
        EntityUid issuerUid;
        NpcSquadTacticsSettings settings;

        if (_npcSquadSystem.TryGetSquad(memberUid, out var squadEntity))
        {
            if (!_npcSquadSystem.TryGetLeader(squadEntity.Value, out var leader))
                return;

            issuerUid = squadEntity.Value.Owner;
            settings = leader.Comp.Tactics;
        }
        else if (_squadMemberQuery.TryComp(memberUid, out var squadMemberComponent))
        {
            issuerUid = memberUid;
            settings = squadMemberComponent.Tactics;
        }
        else
        {
            return;
        }

        // Only a push goes by a blast.
        if (!settings.Pushing)
            return;

        var fightComponent = EnsureComp<NpcSquadFightComponent>(issuerUid);
        fightComponent.LastBlastAt = _gameTiming.CurTime;
        fightComponent.LastBlastCoordinates = coordinates;
    }

    /// <summary>
    ///     Whether a push or a flank is on, or ways round are being looked for: what has to be kept up or ended even by a squad
    ///         too small to start another.
    /// </summary>
    private bool IsFightLive(EntityUid issuerUid)
    {
        return _fightQuery.TryComp(issuerUid, out var fightComponent) && IsFightLive(fightComponent);
    }

    /// <summary>
    ///     With nobody in sight of the hostile: keeps a flank on, flankers following their routes, for
    ///         <see cref="FlankSightGrace"/> after anyone last had it in sight, as the front ducking back into cover for a
    ///         moment is no reason to call them back. (A push is never on alongside a flank.) True if the flank is still on,
    ///         and nothing else should give orders. Not with a flanker gone (<see cref="IsFlankerGone"/>): there is nobody
    ///         to keep going, and one deleted has no place to measure its way from.
    /// </summary>
    private bool KeepFlankWhileUnseen(EntityUid issuerUid, TimeSpan now)
    {
        if (!_fightQuery.TryComp(issuerUid, out var fightComponent) ||
            fightComponent.FlankTargetUid == null ||
            now - fightComponent.LastFoughtAt >= FlankSightGrace)
            return false;

        foreach (var route in fightComponent.FlankRoutes)
        {
            if (IsFlankerGone(route.FlankerUid))
                return false;
        }

        using (GivePlanOrders())
        {
            FollowFlankRoutes(issuerUid, fightComponent, now);
        }

        return true;
    }

    /// <summary>
    ///     Whether a flank is on: flankers on their way round.
    /// </summary>
    private bool IsFlanking(EntityUid issuerUid)
    {
        return _fightQuery.TryComp(issuerUid, out var fightComponent) && fightComponent.FlankTargetUid != null;
    }

    private static bool IsFightLive(NpcSquadFightComponent fightComponent)
    {
        return fightComponent.PushTargetUid != null || fightComponent.FlankTargetUid != null || fightComponent.FlankProbes.Count > 0;
    }

    /// <summary>
    ///     Starts, keeps up or ends a push and a flank on the hostile most of the squad is fighting, which
    ///         <see cref="GatherFight"/> has just found.
    /// </summary>
    private void UpdateFight(EntityUid issuerUid, EntityUid? leaderUid, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        var fightComponent = EnsureComp<NpcSquadFightComponent>(issuerUid);

        // Only those waiting for their meds have it in sight: nobody to fight it.
        if (!_hasFighters)
        {
            EndPush(issuerUid, fightComponent, settings, now);
            EndFlank(issuerUid, fightComponent, settings, now);
            return;
        }

        fightComponent.LastFoughtAt = now;

        var hostileUid = _fightHostileUid;
        var hostileCoordinates = _transformSystem.GetMapCoordinates(hostileUid);

        // A live grenade about: nobody goes anywhere it is, and anyone on the way already stops. Getting away from it is
        //      each member's own (grenade.yml), and then nothing would send it back into the blast. With nothing going, it
        //      is only looked for where one would start.
        if (IsFightLive(fightComponent) && IsGrenadeAbout(fightComponent, hostileCoordinates))
        {
            EndPush(issuerUid, fightComponent, settings, now);
            EndFlank(issuerUid, fightComponent, settings, now);
            return;
        }

        using (GivePlanOrders())
        {
            UpdatePush(issuerUid, leaderUid, fightComponent, hostileUid, hostileCoordinates, settings, now);
            UpdateFlank(issuerUid, leaderUid, fightComponent, hostileUid, hostileCoordinates, settings, now);
        }
    }

    /// <summary>
    ///     Ends a push and a flank, if either is on, for a fight that is over. Only the orders they gave go: one a plan has
    ///         given since stays.
    /// </summary>
    private void EndFight(EntityUid issuerUid, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (!_fightQuery.TryComp(issuerUid, out var fightComponent))
            return;

        EndPush(issuerUid, fightComponent, settings, now);
        EndFlank(issuerUid, fightComponent, settings, now);
    }

    /// <summary>
    ///     Looks at what the group is fighting: whether anyone in it (those waiting for their meds to work too) can see a
    ///         hostile it has reacted to, which is what is returned; and the hostile most of them can, with those members
    ///         (<see cref="_fighters"/>), those waiting for their meds left out. Sets <see cref="_hasFighters"/>.
    /// </summary>
    private bool GatherFight()
    {
        _fightHostileUid = default;
        _hasFighters = false;
        _fighters.Clear();
        _fighterCoordinates.Clear();
        _fighterArmed.Clear();
        _fighterShotMasks.Clear();
        _fighterTraitsKnown = false;
        _fightCounts.Clear();
        _fightMembers.Clear();

        var inCombat = false;
        var best = 0;
        foreach (var memberUid in _members)
        {
            if (_npcRecoverySystem.IsRecovering(memberUid))
            {
                inCombat = inCombat || _npcPerceptionSystem.InCombatContact(memberUid);
                continue;
            }

            _fightMembers.Add(memberUid);
            _fightContacts.Clear();
            _npcPerceptionSystem.GetCombatContacts(memberUid, _fightContacts);
            inCombat = inCombat || _fightContacts.Count > 0;

            foreach (var contactUid in _fightContacts)
            {
                var count = _fightCounts.GetValueOrDefault(contactUid) + 1;
                _fightCounts[contactUid] = count;

                if (count <= best)
                    continue;

                best = count;
                _fightHostileUid = contactUid;
            }
        }

        if (best == 0 || TerminatingOrDeleted(_fightHostileUid))
            return inCombat;

        foreach (var memberUid in _fightMembers)
        {
            if (!_npcPerceptionSystem.IsVisible(memberUid, _fightHostileUid))
                continue;

            _fighters.Add(memberUid);
            _fighterCoordinates.Add(_transformSystem.GetMapCoordinates(memberUid));
        }

        _hasFighters = _fighters.Count > 0;
        return inCombat;
    }

    /// <summary>
    ///     Works out, once for this update, which fighters are armed and what their shots stop at. Only asked for by what is
    ///         about to send someone somewhere, which is rare.
    /// </summary>
    private void EnsureFighterTraits()
    {
        if (_fighterTraitsKnown)
            return;

        _fighterTraitsKnown = true;
        _fighterArmed.Clear();
        _fighterShotMasks.Clear();

        foreach (var fighterUid in _fighters)
        {
            var armed = _gunSystem.TryGetGun(fighterUid, out _);
            _fighterArmed.Add(armed);
            _fighterShotMasks.Add(armed ? _npcLineOfFireSystem.GetShotMask(fighterUid) : default);
        }
    }

    /// <summary>
    ///     Whether a live grenade - anyone's, the squad's own included - is near enough to a fighter, or to where a pusher or
    ///         flanker is going, to be walked into.
    /// </summary>
    private bool IsGrenadeAbout(NpcSquadFightComponent fightComponent, MapCoordinates hostileCoordinates)
    {
        var orderSpotsKnown = false;

        var enumerator = EntityQueryEnumerator<ActiveTimerTriggerComponent, ExplosiveComponent, TransformComponent>();
        while (enumerator.MoveNext(out var grenadeUid, out _, out _, out var transformComponent))
        {
            if (transformComponent.MapID != hostileCoordinates.MapId || _containerSystem.IsEntityInContainer(grenadeUid))
                continue;

            var grenadePosition = _transformSystem.GetWorldPosition(transformComponent);
            if ((grenadePosition - hostileCoordinates.Position).Length() > GrenadeSearchRange)
                continue;

            for (var i = 0; i < _fighterCoordinates.Count; i++)
            {
                if ((_fighterCoordinates[i].Position - grenadePosition).Length() <= GrenadeKeepOff)
                    return true;
            }

            if (!orderSpotsKnown)
            {
                orderSpotsKnown = true;
                _orderSpotPositions.Clear();
                AddOrderSpotPositions(fightComponent.Pushers);
                AddOrderSpotPositions(fightComponent.Flankers);
            }

            foreach (var spot in _orderSpotPositions)
            {
                if ((spot - grenadePosition).Length() <= GrenadeKeepOff)
                    return true;
            }
        }

        return false;
    }

    private void AddOrderSpotPositions(List<EntityUid> memberUids)
    {
        foreach (var memberUid in memberUids)
        {
            if (_orderQuery.TryComp(memberUid, out var orderComponent) &&
                orderComponent.Order is { } order &&
                order.Coordinates.IsValid(EntityManager))
                _orderSpotPositions.Add(_transformSystem.ToMapCoordinates(order.Coordinates).Position);
        }
    }

    [SubscribeLocalEvent]
    private void OnFightShutdown(Entity<NpcSquadFightComponent> entity, ref ComponentShutdown args)
    {
        CancelFlankProbes(entity.Comp);
    }

    #region Push

    private void UpdatePush(EntityUid issuerUid,
        EntityUid? leaderUid,
        NpcSquadFightComponent fightComponent,
        EntityUid hostileUid,
        MapCoordinates hostileCoordinates,
        NpcSquadTacticsSettings settings,
        TimeSpan now)
    {
        if (fightComponent.PushTargetUid is { } pushedUid)
        {
            // Over once they are all there, or it has gone on long enough; or the moment it shoots again, as it has reloaded.
            //      Not on a blast: one that fires back half dazed is what the push is for.
            if (pushedUid != hostileUid ||
                now - fightComponent.PushStartedAt >= settings.PushTime ||
                !fightComponent.PushOnBlast && _npcGunfireSystem.TryGetLastShot(pushedUid, out var lastShotAt) && lastShotAt >= fightComponent.PushStartedAt ||
                HaveAllArrived(fightComponent.Pushers))
                EndPush(issuerUid, fightComponent, settings, now);

            return;
        }

        if (!settings.Pushing || fightComponent.FlankTargetUid != null)
            return;

        // A squad grenade going off by it is a moment worth the push whatever the cooldown says (the grenades have their own),
        //      and wants nobody hanging back to keep firing: one member in sight of it is enough, as the rest are going in on
        //      the blast already (NpcGrenadePusher, grenade_throw.yml). A reload is two at least, and one stays.
        var blast = IsBlastWindow(fightComponent, hostileCoordinates, settings, now);

        if (!blast && (now < fightComponent.NextPushAt || _fighters.Count < 2 || !IsReloadWindow(hostileUid, settings, now)) ||
            GetNearestFighterDistance(hostileCoordinates) > settings.PushMaxRange ||
            IsGrenadeAbout(fightComponent, hostileCoordinates))
            return;

        StartPush(issuerUid, leaderUid, fightComponent, hostileUid, hostileCoordinates, blast, settings, now);
    }

    /// <summary>
    ///     Whether <paramref name="hostileUid"/> was shooting a moment ago and has gone quiet: reloading, or out.
    /// </summary>
    private bool IsReloadWindow(EntityUid hostileUid, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (!_npcGunfireSystem.TryGetLastShot(hostileUid, out var lastShotAt))
            return false;

        var quiet = now - lastShotAt;
        return quiet >= settings.PushSilence && quiet <= settings.PushShotMemory;
    }

    /// <summary>
    ///     Whether one of the squad's grenades has just gone off by the hostile.
    /// </summary>
    private bool IsBlastWindow(NpcSquadFightComponent fightComponent,
        MapCoordinates hostileCoordinates,
        NpcSquadTacticsSettings settings,
        TimeSpan now)
    {
        if (now - fightComponent.LastBlastAt > settings.PushBlastWindow || !fightComponent.LastBlastCoordinates.IsValid(EntityManager))
            return false;

        var blastCoordinates = _transformSystem.ToMapCoordinates(fightComponent.LastBlastCoordinates);
        return blastCoordinates.MapId == hostileCoordinates.MapId &&
            (blastCoordinates.Position - hostileCoordinates.Position).Length() <= settings.PushBlastRange;
    }

    /// <summary>
    ///     Every fighter but the one furthest back goes in close, each from its own side; that one keeps firing.
    /// </summary>
    private void StartPush(EntityUid issuerUid,
        EntityUid? leaderUid,
        NpcSquadFightComponent fightComponent,
        EntityUid hostileUid,
        MapCoordinates hostileCoordinates,
        bool onBlast,
        NpcSquadTacticsSettings settings,
        TimeSpan now)
    {
        EnsureFighterTraits();
        var anchorIndex = onBlast ? -1 : GetFurthestFighterIndex(hostileCoordinates);

        // Bearings are kept relative to the way the pushers are from the hostile on the whole, and put in order by that: so
        //      the place where angles wrap round is behind the hostile from them, where nobody is. In order by the plain
        //      angle, two either side of due west (179 and -179 degrees) were a turn apart, and went to the same tile.
        var pusherSum = Vector2.Zero;
        for (var i = 0; i < _fighters.Count; i++)
        {
            if (i == anchorIndex || !_fighterArmed[i])
                continue;

            var offset = _fighterCoordinates[i].Position - hostileCoordinates.Position;
            if (offset.LengthSquared() > 0.0001f)
                pusherSum += Vector2.Normalize(offset);
        }

        var pusherBearing = pusherSum.LengthSquared() > 0.0001f ? pusherSum.ToAngle() : Angle.Zero;

        _pushBearings.Clear();
        for (var i = 0; i < _fighters.Count; i++)
        {
            if (i == anchorIndex || !_fighterArmed[i])
                continue;

            var offset = _fighterCoordinates[i].Position - hostileCoordinates.Position;
            _pushBearings.Add((i, Angle.ShortestDistance(pusherBearing, offset.ToAngle())));
        }

        // Spread round it: no two closer than PushSpread, seen from the hostile.
        _pushBearings.Sort(static (a, b) => a.Bearing.Theta.CompareTo(b.Bearing.Theta));
        for (var i = 1; i < _pushBearings.Count; i++)
        {
            if (_pushBearings[i].Bearing.Theta - _pushBearings[i - 1].Bearing.Theta < PushSpread.Theta)
                _pushBearings[i] = (_pushBearings[i].Index, _pushBearings[i - 1].Bearing + PushSpread);
        }

        fightComponent.Pushers.Clear();
        foreach (var (pusherIndex, bearing) in _pushBearings)
        {
            var pusherUid = _fighters[pusherIndex];
            if (!TryFindStandSpot(_fighterShotMasks[pusherIndex], hostileCoordinates, pusherBearing + bearing, settings.PushStandoff, StandSpotTurns, out var spot))
                continue;

            SetOrder(pusherUid, issuerUid, NpcOrderKind.Push, spot, Angle.Zero, FightOrderRange, hostileUid, now);
            fightComponent.Pushers.Add(pusherUid);
        }

        if (fightComponent.Pushers.Count == 0)
        {
            fightComponent.NextPushAt = now + settings.PushCooldown;
            return;
        }

        fightComponent.PushTargetUid = hostileUid;
        fightComponent.PushStartedAt = now;
        fightComponent.PushOnBlast = onBlast;

        // One push to a blast: one that is over quickly, everyone there, does not start another on the same one.
        if (onBlast)
            fightComponent.LastBlastAt = TimeSpan.Zero;

        var speakerUid = leaderUid is { } leader && _fighters.Contains(leader) ? leader : fightComponent.Pushers[0];
        _npcVoiceSystem.TrySpeakCallout(speakerUid, NpcCallout.Push, PushCalloutCooldown);
    }

    private void EndPush(EntityUid issuerUid, NpcSquadFightComponent fightComponent, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (fightComponent.PushTargetUid == null)
            return;

        foreach (var pusherUid in fightComponent.Pushers)
        {
            ClearFightOrder(pusherUid, issuerUid);
        }

        fightComponent.Pushers.Clear();
        fightComponent.PushTargetUid = null;
        fightComponent.NextPushAt = now + settings.PushCooldown;
    }

    #endregion

    #region Flank

    private void UpdateFlank(EntityUid issuerUid,
        EntityUid? leaderUid,
        NpcSquadFightComponent fightComponent,
        EntityUid hostileUid,
        MapCoordinates hostileCoordinates,
        NpcSquadTacticsSettings settings,
        TimeSpan now)
    {
        if (fightComponent.FlankTargetUid is { } flankedUid)
        {
            // Another hostile now the one most of the squad is on is no end to it while someone is still fighting this one.
            if (flankedUid != hostileUid && !_fightCounts.ContainsKey(flankedUid) ||
                now - fightComponent.FlankStartedAt >= settings.FlankTimeout ||
                IsFlankDone(fightComponent, settings, now))
            {
                EndFlank(issuerUid, fightComponent, settings, now);
                return;
            }

            FollowFlankRoutes(issuerUid, fightComponent, now);
            return;
        }

        if (fightComponent.FlankProbes.Count > 0)
        {
            UpdateFlankProbes(issuerUid, leaderUid, fightComponent, hostileUid, hostileCoordinates, settings, now);
            return;
        }

        if (!settings.Flanking ||
            now < fightComponent.NextFlankAt ||
            now < fightComponent.NextFlankCheckAt ||
            fightComponent.PushTargetUid != null ||
            _fighters.Count < settings.FlankMinFighters)
            return;

        if (!IsSquadOnOneSide(hostileCoordinates, settings, out var squadBearing))
        {
            // A squad spread round it does not close up in a moment.
            fightComponent.NextFlankCheckAt = now + FlankRecheckDelay;
            return;
        }

        if (IsGrenadeAbout(fightComponent, hostileCoordinates))
            return;

        // Not every time: the roll stands until the cooldown is up.
        if (!_robustRandom.Prob(Math.Clamp(settings.FlankChance, 0f, 1f)))
        {
            fightComponent.NextFlankAt = now + settings.FlankCooldown;
            return;
        }

        StartFlankProbes(fightComponent, hostileUid, hostileCoordinates, squadBearing, settings, now);
    }

    /// <summary>
    ///     Whether every fighter is within <see cref="NpcSquadTacticsSettings.FlankArc"/> of the others, seen from the hostile,
    ///         and which way from it they are on the whole.
    /// </summary>
    private bool IsSquadOnOneSide(MapCoordinates hostileCoordinates, NpcSquadTacticsSettings settings, out Angle squadBearing)
    {
        var sum = Vector2.Zero;
        foreach (var fighterCoordinates in _fighterCoordinates)
        {
            var offset = fighterCoordinates.Position - hostileCoordinates.Position;
            if (offset.LengthSquared() > 0.0001f)
                sum += Vector2.Normalize(offset);
        }

        squadBearing = sum.ToAngle();

        if (sum.LengthSquared() < 0.0001f)
            return false;

        var halfArc = settings.FlankArc.Theta / 2;
        foreach (var fighterCoordinates in _fighterCoordinates)
        {
            var bearing = (fighterCoordinates.Position - hostileCoordinates.Position).ToAngle();

            if (Math.Abs(Angle.ShortestDistance(squadBearing, bearing).Theta) > halfArc)
                return false;
        }

        return true;
    }

    /// <summary>
    ///     Looks for ways round to spots by the hostile's side and behind it, each from the armed fighter nearest it, keeping
    ///         off the hostile itself and the squad's own front: through maintenance, round a block, whatever goes round
    ///         rather than through. <see cref="UpdateFlankProbes"/> picks from what they find.
    /// </summary>
    private void StartFlankProbes(NpcSquadFightComponent fightComponent,
        EntityUid hostileUid,
        MapCoordinates hostileCoordinates,
        Angle squadBearing,
        NpcSquadTacticsSettings settings,
        TimeSpan now)
    {
        CancelFlankProbes(fightComponent);
        EnsureFighterTraits();

        HashSet<(EntityUid Grid, Vector2i Tile)>? baseAvoided = null;
        var gridUid = EntityUid.Invalid;
        var inverse = Matrix3x2.Identity;
        var tileSize = 1f;

        foreach (var turn in FlankTurns)
        {
            if (fightComponent.FlankProbes.Count >= MaxFlankProbes)
                break;

            foreach (var range in (ReadOnlySpan<float>) [settings.FlankRange, settings.FlankRange + 2f, settings.FlankRange - 1.5f])
            {
                var direction = (squadBearing + turn).ToVec();
                var point = new MapCoordinates(hostileCoordinates.Position + direction * range, hostileCoordinates.MapId);

                if (GetNearestArmedFighterIndex(point.Position) is not { } flankerIndex ||
                    IsInSquadLineOfFire(flankerIndex, hostileCoordinates, point.Position) ||
                    !IsStandSpot(_fighterShotMasks[flankerIndex], hostileCoordinates, point, out var spot))
                    continue;

                var flankerUid = _fighters[flankerIndex];

                // The same for every way round but for the room left round the flanker, so only worked out once.
                baseAvoided ??= GetFlankAvoidedBase(hostileCoordinates, out gridUid, out inverse, out tileSize);
                fightComponent.FlankProbeCancel ??= new CancellationTokenSource();

                fightComponent.FlankProbes.Add(new NpcFlankProbe
                {
                    FlankerUid = flankerUid,
                    Spot = spot,
                    Turn = turn,
                    Path = _pathfindingSystem.GetPathAvoiding(flankerUid,
                        Transform(flankerUid).Coordinates,
                        spot,
                        FightOrderRange,
                        GetFlankAvoidedTiles(baseAvoided, gridUid, inverse, tileSize, _fighterCoordinates[flankerIndex].Position),
                        fightComponent.FlankProbeCancel.Token,
                        _pathfindingSystem.GetFlags(flankerUid)),
                });

                break;
            }
        }

        if (fightComponent.FlankProbes.Count == 0)
        {
            fightComponent.NextFlankAt = now + settings.FlankCooldown;
            return;
        }

        fightComponent.FlankProbeTargetUid = hostileUid;
        fightComponent.FlankProbeStartedAt = now;
    }


    /// <summary>
    ///     Stops the searches for ways round that are still going, and forgets them. The one place that cancels and disposes
    ///         <see cref="NpcSquadFightComponent.FlankProbeCancel"/>: <see cref="EndFlank"/> (so for a fight that is over, no
    ///         hostile, or a grenade about), <see cref="UpdateFlankProbes"/> (a timeout, a different hostile, and once the
    ///         answers are in and used), <see cref="StartFlankProbes"/> (before it makes new ones) and the component shutting
    ///         down.
    /// </summary>
    private static void CancelFlankProbes(NpcSquadFightComponent fightComponent)
    {
        fightComponent.FlankProbes.Clear();

        if (fightComponent.FlankProbeCancel is not { } source)
            return;

        fightComponent.FlankProbeCancel = null;
        source.Cancel();
        source.Dispose();
    }

    /// <summary>
    ///     The tiles a way round keeps off, as walls, whichever fighter goes: those by the hostile, and the squad's own front,
    ///         between each fighter and the hostile. Worked out in the hostile's grid's own space, with the grid's inverse
    ///         matrix taken once, from the fighter positions of this update (<see cref="_fighterCoordinates"/>).
    ///     <para>
    ///         Built once by <see cref="StartFlankProbes"/> and never handed to a path job itself: the job adds this set to the
    ///             walls it searches round (<c>GetPathAvoiding</c>) on a worker thread, long after this update. Each probe gets a
    ///             copy of its own from <see cref="GetFlankAvoidedTiles"/>, so one probe's hole round its flanker, and the job
    ///             reading it, never touch another's or this one.
    ///     </para>
    /// </summary>
    private HashSet<(EntityUid Grid, Vector2i Tile)> GetFlankAvoidedBase(MapCoordinates hostileCoordinates,
        out EntityUid gridUid,
        out Matrix3x2 inverse,
        out float tileSize)
    {
        var avoided = new HashSet<(EntityUid Grid, Vector2i Tile)>();
        inverse = Matrix3x2.Identity;
        tileSize = 1f;

        if (!_mapSystem.TryFindGridAt(hostileCoordinates, out gridUid, out var gridComponent))
            return avoided;

        tileSize = gridComponent.TileSize;
        inverse = _transformSystem.GetInvWorldMatrix(gridUid);

        var reach = FlankHostileKeepOff;
        foreach (var fighterCoordinates in _fighterCoordinates)
        {
            reach = MathF.Max(reach, (fighterCoordinates.Position - hostileCoordinates.Position).Length() + 1f);
        }

        var hostileTile = _mapSystem.TileIndicesFor(gridUid, gridComponent, hostileCoordinates);
        var tileReach = (int) MathF.Ceiling(reach);
        var hostileLocal = Vector2.Transform(hostileCoordinates.Position, inverse);

        // Round the hostile.
        var discReach = (int) MathF.Ceiling(FlankHostileKeepOff / tileSize) + 1;
        for (var x = -discReach; x <= discReach; x++)
        {
            for (var y = -discReach; y <= discReach; y++)
            {
                var tile = hostileTile + new Vector2i(x, y);
                if ((GetTileCenter(tile, tileSize) - hostileLocal).Length() <= FlankHostileKeepOff && IsInFlankSquare(tile, hostileTile, tileReach))
                    avoided.Add((gridUid, tile));
            }
        }

        // Along each fighter's line to it.
        foreach (var fighterCoordinates in _fighterCoordinates)
        {
            var from = Vector2.Transform(fighterCoordinates.Position, inverse);
            var segment = hostileLocal - from;
            var segmentLengthSquared = MathF.Max(segment.LengthSquared(), 0.0001f);

            var margin = FlankFrontWidth + tileSize;
            var minTile = new Vector2i((int) MathF.Floor((MathF.Min(from.X, hostileLocal.X) - margin) / tileSize),
                (int) MathF.Floor((MathF.Min(from.Y, hostileLocal.Y) - margin) / tileSize));
            var maxTile = new Vector2i((int) MathF.Floor((MathF.Max(from.X, hostileLocal.X) + margin) / tileSize),
                (int) MathF.Floor((MathF.Max(from.Y, hostileLocal.Y) + margin) / tileSize));

            for (var x = Math.Max(minTile.X, hostileTile.X - tileReach); x <= Math.Min(maxTile.X, hostileTile.X + tileReach); x++)
            {
                for (var y = Math.Max(minTile.Y, hostileTile.Y - tileReach); y <= Math.Min(maxTile.Y, hostileTile.Y + tileReach); y++)
                {
                    var tile = new Vector2i(x, y);
                    var position = GetTileCenter(tile, tileSize);
                    var along = Math.Clamp(Vector2.Dot(position - from, segment) / segmentLengthSquared, 0f, 1f);

                    if ((from + segment * along - position).Length() <= FlankFrontWidth)
                        avoided.Add((gridUid, tile));
                }
            }
        }

        return avoided;
    }

    /// <summary>
    ///     <paramref name="baseAvoided"/> for one flanker: not the tiles by the flanker itself, which starts in the squad's
    ///         front. A copy that is the probe's own, as its path job reads (and adds to its walls from) it on a worker thread
    ///         after this update; <paramref name="baseAvoided"/> is not touched, and the next update's is a new one.
    /// </summary>
    private static HashSet<(EntityUid Grid, Vector2i Tile)> GetFlankAvoidedTiles(HashSet<(EntityUid Grid, Vector2i Tile)> baseAvoided,
        EntityUid gridUid,
        Matrix3x2 inverse,
        float tileSize,
        Vector2 flankerPosition)
    {
        var avoided = new HashSet<(EntityUid Grid, Vector2i Tile)>(baseAvoided);

        if (baseAvoided.Count == 0)
            return avoided;

        var flankerLocal = Vector2.Transform(flankerPosition, inverse);
        var flankerTile = new Vector2i((int) MathF.Floor(flankerLocal.X / tileSize), (int) MathF.Floor(flankerLocal.Y / tileSize));
        var reach = (int) MathF.Ceiling(FlankStartClearance / tileSize) + 1;

        for (var x = -reach; x <= reach; x++)
        {
            for (var y = -reach; y <= reach; y++)
            {
                var tile = flankerTile + new Vector2i(x, y);
                if ((GetTileCenter(tile, tileSize) - flankerLocal).Length() <= FlankStartClearance)
                    avoided.Remove((gridUid, tile));
            }
        }

        return avoided;
    }

    private static Vector2 GetTileCenter(Vector2i tile, float tileSize)
    {
        return new Vector2(tile.X + 0.5f, tile.Y + 0.5f) * tileSize;
    }

    private static bool IsInFlankSquare(Vector2i tile, Vector2i hostileTile, int tileReach)
    {
        return Math.Abs(tile.X - hostileTile.X) <= tileReach && Math.Abs(tile.Y - hostileTile.Y) <= tileReach;
    }

    /// <summary>
    ///     Once every way round has been found or not: the shortest that stays out of the hostile's sight goes, one flanker,
    ///         two from five fighting up, round different sides where it can.
    /// </summary>
    private void UpdateFlankProbes(EntityUid issuerUid,
        EntityUid? leaderUid,
        NpcSquadFightComponent fightComponent,
        EntityUid hostileUid,
        MapCoordinates hostileCoordinates,
        NpcSquadTacticsSettings settings,
        TimeSpan now)
    {
        if (fightComponent.FlankProbeTargetUid != hostileUid || now - fightComponent.FlankProbeStartedAt >= FlankProbeTimeout)
        {
            CancelFlankProbes(fightComponent);
            fightComponent.NextFlankAt = now + settings.FlankCooldown;
            return;
        }

        foreach (var probe in fightComponent.FlankProbes)
        {
            if (!probe.Path.IsCompleted)
                return;
        }

        var wanted = _fighters.Count >= 5 ? 2 : 1;
        NpcFlankProbe? first = null;
        NpcFlankProbe? second = null;
        List<PathPoly>? firstPath = null;
        List<PathPoly>? secondPath = null;
        var firstScore = float.MaxValue;
        var secondScore = float.MaxValue;

        foreach (var probe in fightComponent.FlankProbes)
        {
            // Nothing dearer than the second best is of any use.
            if (!TryScoreFlankRoute(probe, hostileCoordinates, settings, secondScore, out var path, out var score))
                continue;

            if (score < firstScore)
            {
                if (first != null && IsOtherSide(first, probe))
                    (second, secondPath, secondScore) = (first, firstPath, firstScore);

                (first, firstPath, firstScore) = (probe, path, score);
            }
            else if (score < secondScore && first != null && IsOtherSide(first, probe))
            {
                (second, secondPath, secondScore) = (probe, path, score);
            }
        }

        CancelFlankProbes(fightComponent);

        if (first == null || firstPath == null)
        {
            fightComponent.NextFlankAt = now + settings.FlankCooldown;
            return;
        }

        // The second best was picked against whichever was best at the time, and a better way found since may be the same
        //      flanker's, or round the same side: one flanker cannot walk two ways, so then only the best goes.
        if (second != null && !IsOtherSide(first, second))
            second = null;

        fightComponent.Flankers.Clear();
        fightComponent.FlankRoutes.Clear();
        AddFlankRoute(fightComponent, first, firstPath);

        if (wanted > 1 && second != null && secondPath != null)
            AddFlankRoute(fightComponent, second, secondPath);

        fightComponent.FlankTargetUid = hostileUid;
        fightComponent.FlankStartedAt = now;

        using (GivePlanOrders())
        {
            FollowFlankRoutes(issuerUid, fightComponent, now);
        }

        // Said by one keeping it pinned, if any: the one sending them, not one going.
        var speakerUid = leaderUid is { } leader && _fighters.Contains(leader) && !fightComponent.Flankers.Contains(leader)
            ? leader
            : GetFirstNonFlanker(fightComponent) ?? fightComponent.Flankers[0];
        _npcVoiceSystem.TrySpeakCallout(speakerUid, NpcCallout.Flank, FlankCalloutCooldown);
    }

    /// <summary>
    ///     Whether <paramref name="other"/> is a different flanker going round the other side from <paramref name="probe"/>.
    /// </summary>
    private static bool IsOtherSide(NpcFlankProbe probe, NpcFlankProbe other)
    {
        return probe.FlankerUid != other.FlankerUid && Math.Sign(probe.Turn.Theta) != Math.Sign(other.Turn.Theta);
    }

    /// <summary>
    ///     Whether a way found round is a flank at all, and what it costs: its length, and dearer the more of it the hostile
    ///         can see. Not one that is too long, or goes through a door the flanker cannot open; one past the hostile, or
    ///         through the squad's front, was never found (<see cref="GetFlankAvoidedBase"/>). Nor one whose length alone
    ///         is <paramref name="cutoff"/> or more, which no sight could make cheaper than what is already found.
    /// </summary>
    private bool TryScoreFlankRoute(NpcFlankProbe probe,
        MapCoordinates hostileCoordinates,
        NpcSquadTacticsSettings settings,
        float cutoff,
        [NotNullWhen(true)] out List<PathPoly>? path,
        out float score)
    {
        path = null;
        score = float.MaxValue;

        var flankerIndex = _fighters.IndexOf(probe.FlankerUid);
        if (!probe.Path.IsCompletedSuccessfully || TerminatingOrDeleted(probe.FlankerUid) || flankerIndex < 0)
            return false;

#pragma warning disable RA0004 // checked above: it has finished, so this cannot wait on anything
        var result = probe.Path.Result;
#pragma warning restore RA0004

        if (result.Result != PathResult.Path || result.Path.Count == 0 || _npcDoorSystem.RunsThroughShutDoor(probe.FlankerUid, result.Path))
            return false;

        // How long it is first: most are turned away by that alone, before a single sight check.
        var length = 0f;
        var previous = _fighterCoordinates[flankerIndex].Position;
        _routePoints.Clear();

        foreach (var poly in result.Path)
        {
            var point = _transformSystem.ToMapCoordinates(poly.Coordinates);
            length += (point.Position - previous).Length();
            previous = point.Position;
            _routePoints.Add(point);
        }

        if (length > settings.FlankMaxWalk || length >= cutoff)
            return false;

        var seen = 0;
        var looked = 0;

        // The last stretch has to be in sight of it, to shoot from: only the way there counts.
        var lookedUpTo = result.Path.Count - FlankFinalStretch;

        for (var i = 0; i < lookedUpTo; i++)
        {
            looked++;
            if (_npcLineOfSightSystem.InLineOfSight(hostileCoordinates, _routePoints[i], FlankSightRange))
                seen++;
        }

        var exposure = looked == 0 ? 0f : (float) seen / looked;
        path = result.Path;
        score = length * (1f + FlankExposureCost * exposure);
        return true;
    }

    /// <summary>
    ///     The turns of <paramref name="path"/>, every few tiles, as waypoints to send the flanker along one by one: walking
    ///         straight to the end, it would take the short way, past the hostile.
    /// </summary>
    private void AddFlankRoute(NpcSquadFightComponent fightComponent, NpcFlankProbe probe, List<PathPoly> path)
    {
        var route = new NpcFlankRoute { FlankerUid = probe.FlankerUid };

        for (var i = FlankWaypointStep - 1; i < path.Count - 1; i += FlankWaypointStep)
        {
            route.Waypoints.Add(path[i].Coordinates);
        }

        route.Waypoints.Add(probe.Spot);

        // How far each has to go on from there, so that how far is left is one distance and a lookup.
        for (var i = 0; i < route.Waypoints.Count; i++)
        {
            route.TailLengths.Add(0f);
        }

        for (var i = route.Waypoints.Count - 2; i >= 0; i--)
        {
            route.TailLengths[i] = route.TailLengths[i + 1] +
                (route.Waypoints[i].TryDistance(EntityManager, _transformSystem, route.Waypoints[i + 1], out var leg) ? leg : 0f);
        }

        route.StartRemaining = GetRemaining(route);

        fightComponent.Flankers.Add(probe.FlankerUid);
        fightComponent.FlankRoutes.Add(route);
    }

    /// <summary>
    ///     Moves each flanker on to its next waypoint once it is near enough the one it was going to, and keeps its order
    ///         at the one it is going to now.
    /// </summary>
    private void FollowFlankRoutes(EntityUid issuerUid, NpcSquadFightComponent fightComponent, TimeSpan now)
    {
        foreach (var route in fightComponent.FlankRoutes)
        {
            while (route.Next < route.Waypoints.Count - 1 && GetDistance(route.FlankerUid, route.Waypoints[route.Next]) <= FlankWaypointReach)
            {
                route.Next++;
            }

            SetOrder(route.FlankerUid,
                issuerUid,
                NpcOrderKind.Flank,
                route.Waypoints[route.Next],
                Angle.Zero,
                FightOrderRange,
                fightComponent.FlankTargetUid,
                now);
        }
    }

    /// <summary>
    ///     How far a flanker still has to go along its way, in tiles.
    /// </summary>
    private float GetRemaining(NpcFlankRoute route)
    {
        return GetDistance(route.FlankerUid, route.Waypoints[route.Next]) + route.TailLengths[route.Next];
    }

    private int? GetNearestArmedFighterIndex(Vector2 point)
    {
        int? nearestIndex = null;
        var nearest = float.MaxValue;

        for (var i = 0; i < _fighters.Count; i++)
        {
            if (!_fighterArmed[i])
                continue;

            var distance = (_fighterCoordinates[i].Position - point).Length();
            if (distance >= nearest)
                continue;

            nearest = distance;
            nearestIndex = i;
        }

        return nearestIndex;
    }

    /// <summary>
    ///     Whether <paramref name="point"/> is in the way of a fighter other than the one at <paramref name="exceptIndex"/>
    ///         shooting the hostile.
    /// </summary>
    private bool IsInSquadLineOfFire(int exceptIndex, MapCoordinates hostileCoordinates, Vector2 point)
    {
        for (var i = 0; i < _fighters.Count; i++)
        {
            if (i == exceptIndex)
                continue;

            var lane = new NpcFireLane(_fighterCoordinates[i].Position, hostileCoordinates.Position);
            if (NpcSquadFireLaneSystem.IsInLane(lane, point))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether the flank has done what it will: every flanker at the end of its way; or one that has got no nearer, given
    ///         time, cannot get round that way.
    /// </summary>
    private bool IsFlankDone(NpcSquadFightComponent fightComponent, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        var allThere = true;
        var checkProgress = now - fightComponent.FlankStartedAt >= settings.FlankProgressTime;

        foreach (var route in fightComponent.FlankRoutes)
        {
            if (IsFlankerGone(route.FlankerUid))
                return true;

            var remaining = GetRemaining(route);
            if (route.Next < route.Waypoints.Count - 1 || remaining > FightOrderRange + 0.5f)
                allThere = false;

            if (checkProgress && remaining > route.StartRemaining - 1f)
                return true;
        }

        return allThere;
    }

    /// <summary>
    ///     Whether a flanker is gone: deleted, dead, out of the squad, or waiting for its meds. Not out of sight of the hostile:
    ///         a flank route is chosen to be, and a flanker dropping out of the fighters the moment it set off was every
    ///         flank's end. Goes by <see cref="_fightMembers"/>, so only after <see cref="GatherFight"/> has run for the group,
    ///         which it has whenever a flank is on (see <c>UpdateGroup</c>).
    /// </summary>
    private bool IsFlankerGone(EntityUid flankerUid)
    {
        return TerminatingOrDeleted(flankerUid) || !_fightMembers.Contains(flankerUid);
    }

    private void EndFlank(EntityUid issuerUid, NpcSquadFightComponent fightComponent, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        // Ways round still being looked for go too, flank or not yet.
        var lookingForWays = fightComponent.FlankProbes.Count > 0;
        CancelFlankProbes(fightComponent);

        if (fightComponent.FlankTargetUid == null)
        {
            if (lookingForWays)
                fightComponent.NextFlankAt = now + settings.FlankCooldown;

            return;
        }

        foreach (var flankerUid in fightComponent.Flankers)
        {
            ClearFightOrder(flankerUid, issuerUid);
        }

        fightComponent.Flankers.Clear();
        fightComponent.FlankRoutes.Clear();
        fightComponent.FlankTargetUid = null;
        fightComponent.NextFlankAt = now + settings.FlankCooldown;
    }

    private EntityUid? GetFirstNonFlanker(NpcSquadFightComponent fightComponent)
    {
        foreach (var fighterUid in _fighters)
        {
            if (!fightComponent.Flankers.Contains(fighterUid))
                return fighterUid;
        }

        return null;
    }

    #endregion

    /// <summary>
    ///     Somewhere to stand <paramref name="distance"/> tiles from the hostile, about <paramref name="bearing"/> round it,
    ///         with a shot at it for a gun that stops at <paramref name="shotMask"/>: turned a little, or further out, if that
    ///         is a wall.
    /// </summary>
    private bool TryFindStandSpot(CollisionGroup shotMask,
        MapCoordinates hostileCoordinates,
        Angle bearing,
        float distance,
        Angle[] turns,
        out EntityCoordinates spot)
    {
        spot = default;

        for (var further = 0; further < 3; further++)
        {
            foreach (var turn in turns)
            {
                var direction = (bearing + turn).ToVec();
                var point = new MapCoordinates(hostileCoordinates.Position + direction * (distance + further), hostileCoordinates.MapId);

                if (IsStandSpot(shotMask, hostileCoordinates, point, out spot))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Whether <paramref name="point"/> is floor, with a clear shot from there at the hostile for a gun that stops at
    ///         <paramref name="shotMask"/>, and where that is on its grid. The shot is looked at from the hostile's end: a ray
    ///         from inside a wall does not see the wall it starts in, so a spot in one would pass.
    /// </summary>
    private bool IsStandSpot(CollisionGroup shotMask, MapCoordinates hostileCoordinates, MapCoordinates point, out EntityCoordinates spot)
    {
        spot = default;

        if (!_mapSystem.TryFindGridAt(point, out var gridUid, out var gridComponent))
            return false;

        var tile = _mapSystem.TileIndicesFor(gridUid, gridComponent, point);
        if (!_mapSystem.TryGetTileRef(gridUid, gridComponent, tile, out var tileRef) || tileRef.Tile.IsEmpty ||
            !_npcLineOfFireSystem.IsClear(hostileCoordinates, point, shotMask))
            return false;

        spot = _mapSystem.GridTileToLocal(gridUid, gridComponent, tile);
        return true;
    }

    private bool HaveAllArrived(List<EntityUid> memberUids)
    {
        foreach (var memberUid in memberUids)
        {
            if (!_orderQuery.TryComp(memberUid, out var orderComponent) || orderComponent.Order is not { } order)
                continue;

            if (GetDistance(memberUid, order.Coordinates) > FightOrderRange + 0.5f)
                return false;
        }

        return true;
    }

    private float GetDistance(EntityUid uid, EntityCoordinates coordinates)
    {
        return Transform(uid).Coordinates.TryDistance(EntityManager, _transformSystem, coordinates, out var distance)
            ? distance
            : float.MaxValue;
    }

    private float GetNearestFighterDistance(MapCoordinates hostileCoordinates)
    {
        var nearest = float.MaxValue;
        foreach (var fighterCoordinates in _fighterCoordinates)
        {
            nearest = MathF.Min(nearest, (fighterCoordinates.Position - hostileCoordinates.Position).Length());
        }

        return nearest;
    }

    private int GetFurthestFighterIndex(MapCoordinates hostileCoordinates)
    {
        var furthestIndex = 0;
        var furthest = -1f;
        for (var i = 0; i < _fighterCoordinates.Count; i++)
        {
            var distance = (_fighterCoordinates[i].Position - hostileCoordinates.Position).Length();
            if (distance <= furthest)
                continue;

            furthest = distance;
            furthestIndex = i;
        }

        return furthestIndex;
    }

    /// <summary>
    ///     Clears <paramref name="memberUid"/>'s push or flank order from <paramref name="issuerUid"/>, and nothing else: an order
    ///         a plan has given it since stays.
    /// </summary>
    private void ClearFightOrder(EntityUid memberUid, EntityUid issuerUid)
    {
        if (_orderQuery.TryComp(memberUid, out var orderComponent) &&
            orderComponent.Order is { Kind: NpcOrderKind.Push or NpcOrderKind.Flank } order &&
            order.IssuerUid == issuerUid)
            ClearOrder((memberUid, orderComponent));
    }

    private static bool IsFightOrder(NpcOrderComponent orderComponent)
    {
        return orderComponent.Order is { Kind: NpcOrderKind.Push or NpcOrderKind.Flank };
    }
}
