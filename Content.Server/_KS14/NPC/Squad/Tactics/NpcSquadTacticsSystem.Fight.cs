using System.Numerics;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Voices;
using Content.Shared._KS14.NPC;
using Robust.Shared.Map;
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
    ///     How close to its spot a pusher or flanker has to be for that to count as there.
    /// </summary>
    private const float FightOrderRange = 1f;

    /// <summary>
    ///     The least angle between two pushers' ways in, seen from the hostile, in degrees: so they come at it from around it,
    ///         and not all to one tile.
    /// </summary>
    private const float PushSpread = 25f;

    private static readonly float[] StandSpotTurns = [0f, 15f, -15f, 30f, -30f];
    private static readonly float[] FlankSpotTurns = [0f, 10f, -10f, 20f, -20f, 30f, -30f];

    private static readonly TimeSpan PushCalloutCooldown = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan FlankCalloutCooldown = TimeSpan.FromSeconds(10);

    private readonly List<EntityUid> _fightContacts = new();
    private readonly Dictionary<EntityUid, int> _fightCounts = new();
    private readonly List<EntityUid> _fighters = new();
    private readonly List<(EntityUid Uid, float Bearing)> _pushBearings = new();

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
            if (fighterUid == anchorUid)
                continue;

            var offset = _transformSystem.GetMapCoordinates(fighterUid).Position - hostileCoordinates.Position;
            _pushBearings.Add((fighterUid, MathF.Atan2(offset.Y, offset.X) * 180f / MathF.PI));
        }

        // Spread round it: no two closer than PushSpread, seen from the hostile.
        _pushBearings.Sort((a, b) => a.Bearing.CompareTo(b.Bearing));
        for (var i = 1; i < _pushBearings.Count; i++)
        {
            if (_pushBearings[i].Bearing - _pushBearings[i - 1].Bearing < PushSpread)
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
                EndFlank(issuerUid, fightComponent, settings, now);

            return;
        }

        if (!settings.Flanking ||
            now < fightComponent.NextFlankAt ||
            fightComponent.PushTargetUid != null ||
            _fighters.Count < settings.FlankMinFighters ||
            !IsSquadOnOneSide(hostileCoordinates, settings, out var squadBearing))
            return;

        StartFlank(issuerUid, leaderUid, fightComponent, hostileUid, hostileCoordinates, squadBearing, settings, now);
    }

    /// <summary>
    ///     Whether every fighter is within <see cref="NpcSquadTacticsSettings.FlankArc"/> of the others, seen from the hostile,
    ///         and which way from it they are on the whole, in degrees.
    /// </summary>
    private bool IsSquadOnOneSide(MapCoordinates hostileCoordinates, NpcSquadTacticsSettings settings, out float squadBearing)
    {
        var sum = Vector2.Zero;
        foreach (var fighterUid in _fighters)
        {
            var offset = _transformSystem.GetMapCoordinates(fighterUid).Position - hostileCoordinates.Position;
            if (offset.LengthSquared() > 0.0001f)
                sum += Vector2.Normalize(offset);
        }

        squadBearing = MathF.Atan2(sum.Y, sum.X) * 180f / MathF.PI;

        if (sum.LengthSquared() < 0.0001f)
            return false;

        foreach (var fighterUid in _fighters)
        {
            var offset = _transformSystem.GetMapCoordinates(fighterUid).Position - hostileCoordinates.Position;
            var bearing = MathF.Atan2(offset.Y, offset.X) * 180f / MathF.PI;

            if (MathF.Abs(WrapDegrees(bearing - squadBearing)) > settings.FlankArc / 2f)
                return false;
        }

        return true;
    }

    /// <summary>
    ///     One fighter round the hostile's side, two from five up, each the nearest to a spot there with a shot at it and out
    ///         of everyone else's line of fire. The first goes whichever side has a spot, a second the other.
    /// </summary>
    private void StartFlank(EntityUid issuerUid,
        EntityUid? leaderUid,
        NpcSquadFightComponent fightComponent,
        EntityUid hostileUid,
        MapCoordinates hostileCoordinates,
        float squadBearing,
        NpcSquadTacticsSettings settings,
        TimeSpan now)
    {
        var wanted = _fighters.Count >= 5 ? 2 : 1;

        fightComponent.Flankers.Clear();
        fightComponent.FlankStartDistances.Clear();

        foreach (var side in (ReadOnlySpan<float>) [90f, -90f])
        {
            if (fightComponent.Flankers.Count >= wanted)
                break;

            if (!TryFindFlankSpot(hostileCoordinates, squadBearing + side, settings, fightComponent.Flankers, out var spot, out var flankerUid))
                continue;

            SetOrder(flankerUid, issuerUid, NpcOrderKind.Flank, spot, Angle.Zero, FightOrderRange, hostileUid, now);
            fightComponent.Flankers.Add(flankerUid);
            fightComponent.FlankStartDistances.Add(GetDistance(flankerUid, spot));
        }

        if (fightComponent.Flankers.Count == 0)
        {
            fightComponent.NextFlankAt = now + settings.FlankCooldown;
            return;
        }

        fightComponent.FlankTargetUid = hostileUid;
        fightComponent.FlankStartedAt = now;

        // Said by one keeping it pinned, if any: the one sending them, not one going.
        var speakerUid = leaderUid is { } leader && _fighters.Contains(leader) && !fightComponent.Flankers.Contains(leader)
            ? leader
            : GetFirstNonFlanker(fightComponent) ?? fightComponent.Flankers[0];
        _npcVoiceSystem.TrySpeakCallout(speakerUid, NpcCallout.Flank, FlankCalloutCooldown);
    }

    private bool TryFindFlankSpot(MapCoordinates hostileCoordinates,
        float bearing,
        NpcSquadTacticsSettings settings,
        List<EntityUid> taken,
        out EntityCoordinates spot,
        out EntityUid flankerUid)
    {
        spot = default;
        flankerUid = default;

        foreach (var turn in FlankSpotTurns)
        {
            var direction = Angle.FromDegrees(bearing + turn).ToVec();
            var point = new MapCoordinates(hostileCoordinates.Position + direction * settings.FlankRange, hostileCoordinates.MapId);

            // The nearest fighter not already going is the one to go.
            EntityUid? nearestUid = null;
            var nearest = float.MaxValue;
            foreach (var fighterUid in _fighters)
            {
                if (taken.Contains(fighterUid))
                    continue;

                var distance = (_transformSystem.GetMapCoordinates(fighterUid).Position - point.Position).Length();
                if (distance >= nearest)
                    continue;

                nearest = distance;
                nearestUid = fighterUid;
            }

            if (nearestUid is not { } candidateUid ||
                !IsStandSpot(candidateUid, hostileCoordinates, point, out spot) ||
                IsInSquadLineOfFire(candidateUid, hostileCoordinates, point.Position))
                continue;

            flankerUid = candidateUid;
            return true;
        }

        return false;
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
    ///     Whether the flank has done what it will: every flanker there; or one that has got no nearer, given time, cannot get
    ///         round that way.
    /// </summary>
    private bool IsFlankDone(NpcSquadFightComponent fightComponent, NpcSquadTacticsSettings settings, TimeSpan now)
    {
        if (HaveAllArrived(fightComponent.Flankers))
            return true;

        if (now - fightComponent.FlankStartedAt < settings.FlankProgressTime)
            return false;

        for (var i = 0; i < fightComponent.Flankers.Count; i++)
        {
            if (!_orderQuery.TryComp(fightComponent.Flankers[i], out var orderComponent) || orderComponent.Order is not { } order)
                return true;

            if (GetDistance(fightComponent.Flankers[i], order.Coordinates) > fightComponent.FlankStartDistances[i] - 1f)
                return true;
        }

        return false;
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
        fightComponent.FlankStartDistances.Clear();
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
    ///         <paramref name="bearing"/> degrees round it, with a shot at it: turned a little, or further out, if that is a wall.
    /// </summary>
    private bool TryFindStandSpot(EntityUid uid,
        MapCoordinates hostileCoordinates,
        float bearing,
        float distance,
        float[] turns,
        out EntityCoordinates spot)
    {
        spot = default;

        for (var further = 0; further < 3; further++)
        {
            foreach (var turn in turns)
            {
                var direction = Angle.FromDegrees(bearing + turn).ToVec();
                var point = new MapCoordinates(hostileCoordinates.Position + direction * (distance + further), hostileCoordinates.MapId);

                if (IsStandSpot(uid, hostileCoordinates, point, out spot))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Whether <paramref name="point"/> is floor, with a clear shot from there at the hostile for <paramref name="uid"/>'s gun,
    ///         and where that is on its grid.
    /// </summary>
    private bool IsStandSpot(EntityUid uid, MapCoordinates hostileCoordinates, MapCoordinates point, out EntityCoordinates spot)
    {
        spot = default;

        if (!_mapSystem.TryFindGridAt(point, out var gridUid, out var gridComponent))
            return false;

        var tile = _mapSystem.TileIndicesFor(gridUid, gridComponent, point);
        if (!_mapSystem.TryGetTileRef(gridUid, gridComponent, tile, out var tileRef) || tileRef.Tile.IsEmpty ||
            !_npcLineOfFireSystem.IsClear(point, hostileCoordinates, _npcLineOfFireSystem.GetShotMask(uid)))
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

    private static float WrapDegrees(float degrees)
    {
        degrees %= 360f;
        return degrees switch
        {
            > 180f => degrees - 360f,
            < -180f => degrees + 360f,
            _ => degrees,
        };
    }
}
