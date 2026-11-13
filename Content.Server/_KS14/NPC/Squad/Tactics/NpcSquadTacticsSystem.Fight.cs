using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Threading;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Voices;
using Content.Server.NPC.Pathfinding;
using Content.Shared._KS14.NPC;
using Content.Shared.Trigger.Components;
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

    private const float FlankWaypointReach = 1.5f;

    private static readonly TimeSpan PushCalloutCooldown = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan FlankCalloutCooldown = TimeSpan.FromSeconds(10);

    private readonly List<EntityUid> _fightContacts = new();
    private readonly Dictionary<EntityUid, int> _fightCounts = new();
    private readonly List<EntityUid> _fighters = new();
    private readonly List<(EntityUid Uid, Angle Bearing)> _pushBearings = new();
    private readonly HashSet<Entity<ActiveTimerTriggerComponent>> _liveGrenades = new();

    /// <summary>
    ///     One of <paramref name="memberUid"/>'s grenades has gone off at <paramref name="coordinates"/>: its squad may push on
    ///         a hostile caught by it.
    /// </summary>
    public void NoteBlast(EntityUid memberUid, EntityCoordinates coordinates)
    {
        var issuerUid = _npcSquadSystem.TryGetSquad(memberUid, out var squadEntity) ? squadEntity.Value.Owner : memberUid;
        var fightComponent = EnsureComp<NpcSquadFightComponent>(issuerUid);
        fightComponent.LastBlastAt = _gameTiming.CurTime;
        fightComponent.LastBlastCoordinates = coordinates;
    }

    /// <summary>
    ///     Starts, keeps up or ends a push and a flank on the hostile most of the squad is fighting.
    /// </summary>
    private void UpdateFight(EntityUid issuerUid, EntityUid? leaderUid, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (!settings.Pushing && !settings.Flanking)
            return;

        var fightComponent = EnsureComp<NpcSquadFightComponent>(issuerUid);

        if (!TryGatherFighters(out var hostileUid))
        {
            EndPush(issuerUid, fightComponent, settings, now);
            EndFlank(issuerUid, fightComponent, settings, now);
            return;
        }

        var hostileCoordinates = _transformSystem.GetMapCoordinates(hostileUid);

        // A live grenade about: nobody goes anywhere it is, and anyone on the way already stops. Getting away from it is
        //      each member's own (grenade.yml), and then nothing would send it back into the blast.
        if (IsGrenadeAbout(fightComponent, hostileCoordinates))
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
    ///     The hostile most members can see and have reacted to, and those members (<see cref="_fighters"/>), those waiting
    ///         for their meds to work left out.
    /// </summary>
    private bool TryGatherFighters(out EntityUid hostileUid)
    {
        hostileUid = default;
        _fighters.Clear();
        _fightCounts.Clear();

        var best = 0;
        foreach (var memberUid in _members)
        {
            if (_npcRecoverySystem.IsRecovering(memberUid))
                continue;

            _fightContacts.Clear();
            _npcPerceptionSystem.GetCombatContacts(memberUid, _fightContacts);

            foreach (var contactUid in _fightContacts)
            {
                var count = _fightCounts.GetValueOrDefault(contactUid) + 1;
                _fightCounts[contactUid] = count;

                if (count <= best)
                    continue;

                best = count;
                hostileUid = contactUid;
            }
        }

        if (best == 0 || TerminatingOrDeleted(hostileUid))
            return false;

        foreach (var memberUid in _members)
        {
            if (!_npcRecoverySystem.IsRecovering(memberUid) && _npcPerceptionSystem.IsVisible(memberUid, hostileUid))
                _fighters.Add(memberUid);
        }

        return _fighters.Count > 0;
    }

    /// <summary>
    ///     Whether a live grenade - anyone's, the squad's own included - is near enough to a fighter, or to where a pusher or
    ///         flanker is going, to be walked into.
    /// </summary>
    private bool IsGrenadeAbout(NpcSquadFightComponent fightComponent, MapCoordinates hostileCoordinates)
    {
        _liveGrenades.Clear();
        _entityLookupSystem.GetEntitiesInRange(hostileCoordinates, GrenadeSearchRange, _liveGrenades);

        foreach (var grenade in _liveGrenades)
        {
            if (!_explosiveQuery.HasComp(grenade.Owner))
                continue;

            var grenadePosition = _transformSystem.GetMapCoordinates(grenade.Owner).Position;

            foreach (var fighterUid in _fighters)
            {
                if ((_transformSystem.GetMapCoordinates(fighterUid).Position - grenadePosition).Length() <= GrenadeKeepOff)
                    return true;
            }

            if (IsOrderSpotNear(fightComponent.Pushers, grenadePosition) || IsOrderSpotNear(fightComponent.Flankers, grenadePosition))
                return true;
        }

        return false;
    }

    private bool IsOrderSpotNear(List<EntityUid> memberUids, Vector2 position)
    {
        foreach (var memberUid in memberUids)
        {
            if (_orderQuery.TryComp(memberUid, out var orderComponent) &&
                orderComponent.Order is { } order &&
                order.Coordinates.IsValid(EntityManager) &&
                (_transformSystem.ToMapCoordinates(order.Coordinates).Position - position).Length() <= GrenadeKeepOff)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> has a gun in hand to go in with: not one halfway through a magazine swap.
    /// </summary>
    private bool IsArmed(EntityUid uid)
    {
        return _gunSystem.TryGetGun(uid, out _);
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
            if (pushedUid != hostileUid ||
                now - fightComponent.PushStartedAt >= settings.PushTime ||
                _npcGunfireSystem.TryGetLastShot(pushedUid, out var lastShotAt) && lastShotAt >= fightComponent.PushStartedAt ||
                HaveAllArrived(fightComponent.Pushers))
                EndPush(issuerUid, fightComponent, settings, now);

            return;
        }

        if (!settings.Pushing ||
            now < fightComponent.NextPushAt ||
            fightComponent.FlankTargetUid != null ||
            _fighters.Count < 2 ||
            GetNearestFighterDistance(hostileCoordinates) > settings.PushMaxRange ||
            !IsReloadWindow(hostileUid, settings, now) && !IsBlastWindow(fightComponent, hostileCoordinates, settings, now))
            return;

        StartPush(issuerUid, leaderUid, fightComponent, hostileUid, hostileCoordinates, settings, now);
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
        NpcSquadTacticsSettings settings,
        TimeSpan now)
    {
        var anchorUid = GetFurthestFighter(hostileCoordinates);

        _pushBearings.Clear();
        foreach (var fighterUid in _fighters)
        {
            if (fighterUid == anchorUid || !IsArmed(fighterUid))
                continue;

            var offset = _transformSystem.GetMapCoordinates(fighterUid).Position - hostileCoordinates.Position;
            _pushBearings.Add((fighterUid, offset.ToAngle()));
        }

        // Spread round it: no two closer than PushSpread, seen from the hostile.
        _pushBearings.Sort((a, b) => a.Bearing.Theta.CompareTo(b.Bearing.Theta));
        for (var i = 1; i < _pushBearings.Count; i++)
        {
            if (_pushBearings[i].Bearing.Theta - _pushBearings[i - 1].Bearing.Theta < PushSpread.Theta)
                _pushBearings[i] = (_pushBearings[i].Uid, _pushBearings[i - 1].Bearing + PushSpread);
        }

        fightComponent.Pushers.Clear();
        foreach (var (pusherUid, bearing) in _pushBearings)
        {
            if (!TryFindStandSpot(pusherUid, hostileCoordinates, bearing, settings.PushStandoff, StandSpotTurns, out var spot))
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
            if (flankedUid != hostileUid || now - fightComponent.FlankStartedAt >= settings.FlankTimeout || IsFlankDone(fightComponent, settings, now))
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
            fightComponent.PushTargetUid != null ||
            _fighters.Count < settings.FlankMinFighters ||
            !IsSquadOnOneSide(hostileCoordinates, settings, out var squadBearing))
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
        foreach (var fighterUid in _fighters)
        {
            var offset = _transformSystem.GetMapCoordinates(fighterUid).Position - hostileCoordinates.Position;
            if (offset.LengthSquared() > 0.0001f)
                sum += Vector2.Normalize(offset);
        }

        squadBearing = sum.ToAngle();

        if (sum.LengthSquared() < 0.0001f)
            return false;

        var halfArc = settings.FlankArc.Theta / 2;
        foreach (var fighterUid in _fighters)
        {
            var bearing = (_transformSystem.GetMapCoordinates(fighterUid).Position - hostileCoordinates.Position).ToAngle();

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
        fightComponent.FlankProbes.Clear();

        foreach (var turn in FlankTurns)
        {
            if (fightComponent.FlankProbes.Count >= MaxFlankProbes)
                break;

            foreach (var range in (ReadOnlySpan<float>) [settings.FlankRange, settings.FlankRange + 2f, settings.FlankRange - 1.5f])
            {
                var direction = (squadBearing + turn).ToVec();
                var point = new MapCoordinates(hostileCoordinates.Position + direction * range, hostileCoordinates.MapId);

                if (GetNearestArmedFighter(point.Position) is not { } flankerUid ||
                    !IsStandSpot(flankerUid, hostileCoordinates, point, out var spot) ||
                    IsInSquadLineOfFire(flankerUid, hostileCoordinates, point.Position))
                    continue;

                fightComponent.FlankProbes.Add(new NpcFlankProbe
                {
                    FlankerUid = flankerUid,
                    Spot = spot,
                    Turn = turn,
                    Path = _pathfindingSystem.GetPathAvoiding(flankerUid,
                        Transform(flankerUid).Coordinates,
                        spot,
                        FightOrderRange,
                        GetFlankAvoidedTiles(flankerUid, hostileCoordinates),
                        CancellationToken.None,
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
    ///     The tiles a way round keeps off, as walls: those by the hostile, and the squad's own front, between each fighter
    ///         and the hostile. Not those by the flanker itself, which starts in that front.
    /// </summary>
    private HashSet<(EntityUid Grid, Vector2i Tile)> GetFlankAvoidedTiles(EntityUid flankerUid, MapCoordinates hostileCoordinates)
    {
        var avoided = new HashSet<(EntityUid Grid, Vector2i Tile)>();

        if (!_mapSystem.TryFindGridAt(hostileCoordinates, out var gridUid, out var gridComponent))
            return avoided;

        var flankerPosition = _transformSystem.GetMapCoordinates(flankerUid).Position;
        var reach = FlankHostileKeepOff;

        foreach (var fighterUid in _fighters)
        {
            reach = MathF.Max(reach, (_transformSystem.GetMapCoordinates(fighterUid).Position - hostileCoordinates.Position).Length() + 1f);
        }

        var hostileTile = _mapSystem.TileIndicesFor(gridUid, gridComponent, hostileCoordinates);
        var tileReach = (int) MathF.Ceiling(reach);

        for (var x = -tileReach; x <= tileReach; x++)
        {
            for (var y = -tileReach; y <= tileReach; y++)
            {
                var tile = hostileTile + new Vector2i(x, y);
                var position = _transformSystem.ToMapCoordinates(_mapSystem.GridTileToLocal(gridUid, gridComponent, tile)).Position;

                if ((position - flankerPosition).Length() <= FlankStartClearance)
                    continue;

                if ((position - hostileCoordinates.Position).Length() <= FlankHostileKeepOff || IsOnSquadFront(position, hostileCoordinates.Position))
                    avoided.Add((gridUid, tile));
            }
        }

        return avoided;
    }

    private bool IsOnSquadFront(Vector2 position, Vector2 hostilePosition)
    {
        foreach (var fighterUid in _fighters)
        {
            var from = _transformSystem.GetMapCoordinates(fighterUid).Position;
            var segment = hostilePosition - from;
            var along = Math.Clamp(Vector2.Dot(position - from, segment) / MathF.Max(segment.LengthSquared(), 0.0001f), 0f, 1f);

            if ((from + segment * along - position).Length() <= FlankFrontWidth)
                return true;
        }

        return false;
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
            fightComponent.FlankProbes.Clear();
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
            if (!TryScoreFlankRoute(probe, hostileCoordinates, settings, out var path, out var score))
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

        fightComponent.FlankProbes.Clear();

        if (first == null || firstPath == null)
        {
            fightComponent.NextFlankAt = now + settings.FlankCooldown;
            return;
        }

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
    ///         through the squad's front, was never found (<see cref="GetFlankAvoidedTiles"/>).
    /// </summary>
    private bool TryScoreFlankRoute(NpcFlankProbe probe,
        MapCoordinates hostileCoordinates,
        NpcSquadTacticsSettings settings,
        [NotNullWhen(true)] out List<PathPoly>? path,
        out float score)
    {
        path = null;
        score = float.MaxValue;

        if (!probe.Path.IsCompletedSuccessfully || TerminatingOrDeleted(probe.FlankerUid) || !_fighters.Contains(probe.FlankerUid))
            return false;

#pragma warning disable RA0004 // checked above: it has finished, so this cannot wait on anything
        var result = probe.Path.Result;
#pragma warning restore RA0004

        if (result.Result != PathResult.Path || result.Path.Count == 0 || _npcDoorSystem.RunsThroughShutDoor(probe.FlankerUid, result.Path))
            return false;

        var length = 0f;
        var previous = _transformSystem.GetMapCoordinates(probe.FlankerUid).Position;
        var seen = 0;
        var looked = 0;

        // The last stretch has to be in sight of it, to shoot from: only the way there counts.
        var lookedUpTo = result.Path.Count - FlankFinalStretch;

        for (var i = 0; i < result.Path.Count; i++)
        {
            var point = _transformSystem.ToMapCoordinates(result.Path[i].Coordinates);
            length += (point.Position - previous).Length();
            previous = point.Position;

            if (i >= lookedUpTo)
                continue;

            looked++;
            if (_npcLineOfSightSystem.InLineOfSight(hostileCoordinates, point, FlankSightRange))
                seen++;
        }

        if (length > settings.FlankMaxWalk)
            return false;

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
        var remaining = GetDistance(route.FlankerUid, route.Waypoints[route.Next]);

        for (var i = route.Next + 1; i < route.Waypoints.Count; i++)
        {
            if (route.Waypoints[i - 1].TryDistance(EntityManager, _transformSystem, route.Waypoints[i], out var leg))
                remaining += leg;
        }

        return remaining;
    }

    private EntityUid? GetNearestArmedFighter(Vector2 point)
    {
        EntityUid? nearestUid = null;
        var nearest = float.MaxValue;

        foreach (var fighterUid in _fighters)
        {
            if (!IsArmed(fighterUid))
                continue;

            var distance = (_transformSystem.GetMapCoordinates(fighterUid).Position - point).Length();
            if (distance >= nearest)
                continue;

            nearest = distance;
            nearestUid = fighterUid;
        }

        return nearestUid;
    }

    /// <summary>
    ///     Whether <paramref name="point"/> is in the way of a fighter other than <paramref name="exceptUid"/> shooting the hostile.
    /// </summary>
    private bool IsInSquadLineOfFire(EntityUid exceptUid, MapCoordinates hostileCoordinates, Vector2 point)
    {
        foreach (var fighterUid in _fighters)
        {
            if (fighterUid == exceptUid)
                continue;

            var lane = new NpcFireLane(_transformSystem.GetMapCoordinates(fighterUid).Position, hostileCoordinates.Position);
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
            if (TerminatingOrDeleted(route.FlankerUid) || !_fighters.Contains(route.FlankerUid))
                return true;

            var remaining = GetRemaining(route);
            if (route.Next < route.Waypoints.Count - 1 || remaining > FightOrderRange + 0.5f)
                allThere = false;

            if (checkProgress && remaining > route.StartRemaining - 1f)
                return true;
        }

        return allThere;
    }

    private void EndFlank(EntityUid issuerUid, NpcSquadFightComponent fightComponent, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (fightComponent.FlankTargetUid == null)
            return;

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
    ///     Somewhere for <paramref name="uid"/> to stand <paramref name="distance"/> tiles from the hostile, about
    ///         <paramref name="bearing"/> round it, with a shot at it: turned a little, or further out, if that is a wall.
    /// </summary>
    private bool TryFindStandSpot(EntityUid uid,
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

                if (IsStandSpot(uid, hostileCoordinates, point, out spot))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Whether <paramref name="point"/> is floor, with a clear shot from there at the hostile for <paramref name="uid"/>'s gun,
    ///         and where that is on its grid. The shot is looked at from the hostile's end: a ray from inside a wall does not
    ///         see the wall it starts in, so a spot in one would pass.
    /// </summary>
    private bool IsStandSpot(EntityUid uid, MapCoordinates hostileCoordinates, MapCoordinates point, out EntityCoordinates spot)
    {
        spot = default;

        if (!_mapSystem.TryFindGridAt(point, out var gridUid, out var gridComponent))
            return false;

        var tile = _mapSystem.TileIndicesFor(gridUid, gridComponent, point);
        if (!_mapSystem.TryGetTileRef(gridUid, gridComponent, tile, out var tileRef) || tileRef.Tile.IsEmpty ||
            !_npcLineOfFireSystem.IsClear(hostileCoordinates, point, _npcLineOfFireSystem.GetShotMask(uid)))
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
        foreach (var fighterUid in _fighters)
        {
            nearest = MathF.Min(nearest, (_transformSystem.GetMapCoordinates(fighterUid).Position - hostileCoordinates.Position).Length());
        }

        return nearest;
    }

    private EntityUid GetFurthestFighter(MapCoordinates hostileCoordinates)
    {
        var furthestUid = _fighters[0];
        var furthest = -1f;
        foreach (var fighterUid in _fighters)
        {
            var distance = (_transformSystem.GetMapCoordinates(fighterUid).Position - hostileCoordinates.Position).Length();
            if (distance <= furthest)
                continue;

            furthest = distance;
            furthestUid = fighterUid;
        }

        return furthestUid;
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
