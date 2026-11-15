using System.Numerics;
using Content.Server._KS14.NPC.LineOfFire;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Server._KS14.NPC.Systems;
using Content.Server._KS14.NPC.Utility;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._KS14.NPC;
using Content.Shared._Mono.Overlays;
using Content.Shared.Inventory;
using Content.Shared.Light.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Lights;

/// <summary>
///     Picks the lights an NPC with <see cref="NpcLightShooterComponent"/> shoots out, and keeps the pick for HTN to read
///         (<c>LightPickPrecondition</c>, <c>LightPickQuery</c>, the "Shoot out a light" branch of the operative root).
///         The shot itself is ordinary ranged combat at a light as the target (<c>GunOperator</c>), so it goes through
///         everything every NPC shot does: line of fire, <see cref="NpcSquadFireLaneSystem.TryGetClearShot"/>, dark aim.
/// </summary>
/// <remarks>
///     <para>
///         <b>How a light breaks.</b> A wall light is a <c>PoweredLight</c> with a bulb in it, <c>Damageable</c> and
///             <c>Injurable</c>, and <c>RequireProjectileTarget</c>: a round only hits it if the light was what was aimed
///             at (<c>TargetedProjectile</c>, set by <c>GunSystem.AttemptShoot</c> from the target the NPC passes). Any
///             damage at all breaks the bulb (<c>SharedPoweredLightSystem.HandleLightDamaged</c>), and a broken bulb puts the
///             light out. So all an NPC needs is the light as the ranged target, aimed at where the fixture is, which is
///             off the entity's own position: <see cref="GetAimPoint"/>.
///     </para>
///     <para>
///         <b>When.</b> Only two moments, both when nothing is in the way of what the NPC would do anyway: staging at a
///             door, for the room's lit lights it can see from where it stands (<see cref="NpcLightShotReason.Staging"/>), and
///             in a fight, for the light on a hostile that cannot see in the dark, when no hostile in sight can be hit
///             (<see cref="NpcLightShotReason.Fight"/>). The operative root puts the branch above the squad plan and combat, where a
///             squad on plan orders still gets to it; that a hostile it can hit is shot instead is this system's to keep. They never look for darkness or prefer dark places.
///     </para>
///     <para>
///         <b>Bounds.</b> One light at a time per NPC and one NPC per light, <see cref="NpcLightShooterComponent.TaskTime"/>
///             to put it out, then <see cref="NpcLightShooterComponent.SuccessCooldown"/> (or
///             <see cref="NpcLightShooterComponent.Cooldown"/> if it did not go out). Never at a light that gives no line of
///             fire or whose shot is not clear of friends.
///     </para>
/// </remarks>
public sealed partial class NpcLightShooterSystem : EntitySystem
{
    /// <summary>
    ///     How close to the middle of its fixture a light is aimed at to be hit, in tiles: what
    ///         <see cref="NpcSquadFireLaneSystem.TryGetClearShot"/> may move the shot by. A light fixture is under a tile wide.
    /// </summary>
    public const float AimTolerance = 0.25f;

    [Dependency] private EntityLookupSystem _lookupSystem = default!;
    [Dependency] private GunSystem _gunSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private InventorySystem _inventorySystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NpcLightDetectionSystem _npcLightDetectionSystem = default!;
    [Dependency] private NpcLineOfFireSystem _npcLineOfFireSystem = default!;
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;
    [Dependency] private NpcSensorSystem _npcSensorSystem = default!;
    [Dependency] private NpcSquadFireLaneSystem _npcSquadFireLaneSystem = default!;
    [Dependency] private NpcSquadTacticsSystem _npcSquadTacticsSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<NpcLightShooterComponent> _lightShooterQuery = default!;
    [Dependency] private EntityQuery<NpcPerceptionComponent> _perceptionQuery = default!;
    [Dependency] private EntityQuery<PhosphorNightVisionRecipientComponent> _nightVisionQuery = default!;
    [Dependency] private EntityQuery<PointLightComponent> _pointLightQuery = default!;
    [Dependency] private EntityQuery<PoweredLightComponent> _poweredLightQuery = default!;

    private readonly HashSet<Entity<PoweredLightComponent>> _lights = new();

    public override void Initialize()
    {
        base.Initialize();

        // The pick is decided before the planner is asked what to do with it.
        UpdatesBefore.Add(typeof(Content.Server.NPC.HTN.HTNSystem));
    }

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<NpcLightShooterComponent> entity, ref MapInitEvent args)
    {
        entity.Comp.NextUpdate = NpcThrottle.Spread(_gameTiming.CurTime, TimeSpan.FromSeconds(entity.Comp.UpdateInterval), _robustRandom);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;
        var enumerator = EntityQueryEnumerator<NpcLightShooterComponent, NpcPerceptionComponent, ActiveNPCComponent>();

        while (enumerator.MoveNext(out var uid, out var shooterComponent, out _, out _))
        {
            if (!NpcThrottle.TryConsume(ref shooterComponent.NextUpdate, now, TimeSpan.FromSeconds(shooterComponent.UpdateInterval)))
                continue;

            UpdateShooter((uid, shooterComponent), now);
        }
    }

    /// <summary>
    ///     Runs one update for <paramref name="uid"/> right now, whether or not it is due and whether or not the NPC is awake.
    ///         For tests.
    /// </summary>
    internal void UpdateNow(EntityUid uid)
    {
        if (_lightShooterQuery.TryComp(uid, out var shooterComponent))
            UpdateShooter((uid, shooterComponent), _gameTiming.CurTime);
    }

    /// <summary>
    ///     The light <paramref name="uid"/> is to shoot out right now, if there is one.
    /// </summary>
    public bool TryGetPick(EntityUid uid, out EntityUid lightUid)
    {
        lightUid = default;

        if (!_lightShooterQuery.TryComp(uid, out var shooterComponent) ||
            shooterComponent.Target is not { } target ||
            !IsLitLight(target) ||
            _gameTiming.CurTime >= shooterComponent.ExpiresAt)
            return false;

        lightUid = target;
        return true;
    }

    /// <summary>
    ///     Where a round has to go to hit <paramref name="lightUid"/>: the middle of its fixture, which for a wall light is
    ///         against the wall and a good way from the entity's own position, at the middle of its tile.
    /// </summary>
    public Vector2 GetAimPoint(EntityUid lightUid)
    {
        var position = _transformSystem.GetWorldPosition(lightUid);

        if (!_fixturesQuery.TryComp(lightUid, out var fixturesComponent))
            return position;

        var transform = _physicsSystem.GetPhysicsTransform(lightUid);
        foreach (var fixture in fixturesComponent.Fixtures.Values)
        {
            // The first is the fixture: a light has one.
            return fixture.Shape.ComputeAABB(transform, 0).Center;
        }

        return position;
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> is something an NPC shoots at to put a light out: <see cref="GetAimPoint"/> and
    ///         <see cref="AimTolerance"/> are the ones to use for it.
    /// </summary>
    public bool IsLight(EntityUid uid)
    {
        return _poweredLightQuery.HasComp(uid);
    }

    private bool IsLitLight(EntityUid lightUid)
    {
        return !TerminatingOrDeleted(lightUid) &&
            _poweredLightQuery.TryComp(lightUid, out var poweredLightComponent) &&
            poweredLightComponent.CurrentLit &&
            _pointLightQuery.TryComp(lightUid, out var pointLightComponent) &&
            pointLightComponent.Enabled;
    }

    private void UpdateShooter(Entity<NpcLightShooterComponent> entity, TimeSpan now)
    {
        var uid = entity.Owner;
        var shooterComponent = entity.Comp;

        if (!_mobStateSystem.IsAlive(uid))
        {
            EndPick(entity, now, lightOut: false, replan: false);
            return;
        }

        if (shooterComponent.Target is { } target)
        {
            // Still going while the light is, and while there is time: whichever ends it, the next waits its turn.
            if (!IsLitLight(target))
                EndPick(entity, now, lightOut: true, replan: true);
            else if (now >= shooterComponent.ExpiresAt)
                EndPick(entity, now, lightOut: false, replan: true);

            return;
        }

        if (now < shooterComponent.NextPickAt)
            return;

        if (!CanShoot(uid))
            return;

        var from = _transformSystem.GetMapCoordinates(uid);

        EntityUid? light = null;
        var reason = NpcLightShotReason.None;

        if (shooterComponent.ShootsInFights && TryPickForFight(entity, from, out var fightLight))
        {
            light = fightLight;
            reason = NpcLightShotReason.Fight;
        }
        else if (shooterComponent.ShootsWhileStaging && TryPickForStaging(entity, from, out var stagingLight))
        {
            light = stagingLight;
            reason = NpcLightShotReason.Staging;
        }

        if (light is not { } picked)
            return;

        shooterComponent.Target = picked;
        shooterComponent.Reason = reason;
        shooterComponent.ExpiresAt = now + TimeSpan.FromSeconds(shooterComponent.TaskTime);

        // Taken up on the next planning, rather than the one after.
        _npcSensorSystem.RequestReplan(uid);
    }

    private void EndPick(Entity<NpcLightShooterComponent> entity, TimeSpan now, bool lightOut, bool replan)
    {
        if (entity.Comp.Target == null)
            return;

        entity.Comp.Target = null;
        entity.Comp.Reason = NpcLightShotReason.None;
        entity.Comp.NextPickAt = now + TimeSpan.FromSeconds(lightOut ? entity.Comp.SuccessCooldown : entity.Comp.Cooldown);

        if (replan)
            _npcSensorSystem.RequestReplan(entity.Owner);
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> holds a gun that has a round to fire.
    /// </summary>
    private bool CanShoot(EntityUid uid)
    {
        if (!_gunSystem.TryGetGun(uid, out var gun))
            return false;

        var ammoEvent = new GetAmmoCountEvent();
        RaiseLocalEvent(gun, ref ammoEvent);
        return ammoEvent.Count > 0;
    }

    #region Fight

    /// <summary>
    ///     A light that shows a hostile that cannot see in the dark, when none in sight can be hit.
    /// </summary>
    private bool TryPickForFight(Entity<NpcLightShooterComponent> entity, MapCoordinates from, out EntityUid lightUid)
    {
        lightUid = default;

        if (!_perceptionQuery.TryComp(entity.Owner, out var perceptionComponent) || perceptionComponent.Contacts.Count == 0)
            return false;

        var now = _gameTiming.CurTime;
        var lostAge = TimeSpan.FromSeconds(entity.Comp.LostContactAge);
        var best = float.MinValue;

        foreach (var (hostileUid, contact) in perceptionComponent.Contacts)
        {
            if (contact.State is not (NpcContactState.Visible or NpcContactState.Lost or NpcContactState.Reported))
                continue;

            if (Deleted(hostileUid) || _npcPerceptionSystem.IsKnownDead(entity.Owner, hostileUid))
                continue;

            var visible = contact.State == NpcContactState.Visible;
            if (!visible && now - contact.LastSeen > lostAge)
                continue;

            var hostileCoordinates = visible
                ? _transformSystem.GetMapCoordinates(hostileUid)
                : _transformSystem.ToMapCoordinates(contact.LastKnownCoordinates);

            if (hostileCoordinates.MapId != from.MapId)
                continue;

            // The shot it has is at the hostile, and the one that counts.
            if (visible && _npcLineOfFireSystem.HasLineOfFire(entity.Owner, hostileUid, (hostileCoordinates.Position - from.Position).Length() + 0.1f))
            {
                lightUid = default;
                return false;
            }

            // Putting a light out is no use against one that sees in the dark, or that is not lit by any of them.
            if (SeesInTheDark(hostileUid, entity.Comp.HostileDarkVision) ||
                visible && _npcLightDetectionSystem.GetLightLevel(hostileUid) < entity.Comp.FightLightLevel)
                continue;

            FindLight(entity, from, hostileCoordinates, requireReach: true, roomTiles: null, gridUid: null, ref best, ref lightUid);
        }

        return best > float.MinValue;
    }

    /// <summary>
    ///     Whether <paramref name="hostileUid"/> sees well enough in the dark for a light going out to make no difference to it:
    ///         through what an NPC wears or has (<see cref="NpcDarkVisionComponent"/>), or night vision goggles in use.
    /// </summary>
    private bool SeesInTheDark(EntityUid hostileUid, float threshold)
    {
        if (_nightVisionQuery.HasComp(hostileUid))
            return true;

        if (_perceptionQuery.TryComp(hostileUid, out var perceptionComponent))
            return perceptionComponent.DarkVision >= threshold;

        var ev = new NpcGetDarkVisionEvent();
        RaiseLocalEvent(hostileUid, ref ev);

        if (TryComp<InventoryComponent>(hostileUid, out var inventoryComponent))
            _inventorySystem.RelayEvent((hostileUid, inventoryComponent), ref ev);

        return ev.DarkVision >= threshold;
    }

    #endregion

    #region Staging

    /// <summary>
    ///     A lit light of the room this squad is staging at the door of that the shooter can see from where it stands.
    /// </summary>
    private bool TryPickForStaging(Entity<NpcLightShooterComponent> entity, MapCoordinates from, out EntityUid lightUid)
    {
        lightUid = default;

        if (!_npcSquadTacticsSystem.TryGetOrder(entity.Owner, out var order) || order.Kind != NpcOrderKind.Stage)
            return false;

        if (_npcSquadTacticsSystem.GetHunt(order.IssuerUid) is not { GridUid: { } gridUid } hunt || hunt.RoomTiles.Count == 0)
            return false;

        return TryPickInRoom(entity, from, gridUid, hunt.RoomTiles, out lightUid);
    }

    /// <summary>
    ///     The nearest lit light on one of <paramref name="roomTiles"/>, of <paramref name="gridUid"/>, that
    ///         <paramref name="entity"/> has a clear shot at from <paramref name="from"/>.
    /// </summary>
    internal bool TryPickInRoom(Entity<NpcLightShooterComponent> entity,
        MapCoordinates from,
        EntityUid gridUid,
        HashSet<Vector2i> roomTiles,
        out EntityUid lightUid)
    {
        lightUid = default;

        var best = float.MinValue;
        FindLight(entity, from, from, requireReach: false, roomTiles, gridUid, ref best, ref lightUid);

        return best > float.MinValue;
    }

    #endregion

    /// <summary>
    ///     The best lit light within the shooter's range that it has a clear shot at, with a higher score than
    ///         <paramref name="best"/>: of those lighting <paramref name="hostileCoordinates"/> (with
    ///         <paramref name="requireReach"/>), nearest it and brightest; of those in <paramref name="roomTiles"/>, nearest the
    ///         shooter.
    /// </summary>
    private void FindLight(Entity<NpcLightShooterComponent> entity,
        MapCoordinates from,
        MapCoordinates hostileCoordinates,
        bool requireReach,
        HashSet<Vector2i>? roomTiles,
        EntityUid? gridUid,
        ref float best,
        ref EntityUid lightUid)
    {
        var range = entity.Comp.Range;
        _lights.Clear();
        _lookupSystem.GetEntitiesInRange(from, range, _lights, LookupFlags.Static | LookupFlags.Approximate);

        MapGridComponent? gridComponent = null;
        if (roomTiles != null && (gridUid == null || !_mapGridQuery.TryComp(gridUid, out gridComponent)))
            return;

        foreach (var light in _lights)
        {
            if (!IsLitLight(light.Owner) || IsClaimedByAnother(entity.Owner, light.Owner))
                continue;

            var aimPoint = GetAimPoint(light.Owner);
            var distance = (aimPoint - from.Position).Length();
            if (distance > range || distance < 0.5f)
                continue;

            var score = -distance;
            if (roomTiles != null)
            {
                var lightCoordinates = _transformSystem.GetMoverCoordinates(light.Owner);
                if (_transformSystem.GetGrid(lightCoordinates) != gridUid ||
                    !roomTiles.Contains(_mapSystem.TileIndicesFor(gridUid!.Value, gridComponent!, lightCoordinates)))
                    continue;
            }
            else
            {
                var pointLightComponent = _pointLightQuery.GetComponent(light.Owner);
                var toHostile = (hostileCoordinates.Position - aimPoint).Length();
                if (requireReach && toHostile > pointLightComponent.Radius)
                    continue;

                // The one doing most for the hostile: the brightest, and the nearer it.
                score = pointLightComponent.Energy / (1f + toHostile * toHostile);
            }

            if (score <= best)
                continue;

            // The dear checks last: only for one that would be the best.
            if (!_npcLineOfFireSystem.HasLineOfFire(entity.Owner, light.Owner, distance + 0.1f) ||
                !_npcSquadFireLaneSystem.TryGetClearShot(entity.Owner, from, new MapCoordinates(aimPoint, from.MapId), AimTolerance, out _))
                continue;

            best = score;
            lightUid = light.Owner;
        }
    }

    private bool IsClaimedByAnother(EntityUid shooterUid, EntityUid lightUid)
    {
        var enumerator = EntityQueryEnumerator<NpcLightShooterComponent>();
        while (enumerator.MoveNext(out var otherUid, out var otherComponent))
        {
            if (otherUid != shooterUid && otherComponent.Target == lightUid)
                return true;
        }

        return false;
    }
}
