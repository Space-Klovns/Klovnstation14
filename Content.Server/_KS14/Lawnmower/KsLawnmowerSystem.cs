using System.Linq;
using System.Numerics;
using Content.Server.Decals;
using Content.Server.Destructible.Thresholds.Behaviors;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Camera;
using Content.Shared.Chat;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Decals;
using Content.Shared.Destructible;
using Content.Shared.Destructible.Thresholds.Triggers;
using Content.Shared.Emag.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Gibbing;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Maps;
using Content.Shared.Zombies;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Lawnmower;

public sealed partial class KsLawnmowerSystem : EntitySystem
{
    [Dependency] private DecalSystem _decalSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private EmagSystem _emagSystem = default!;
    [Dependency] private GibbingSystem _gibbingSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private ITileDefinitionManager _tileDefinitions = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private MobThresholdSystem _mobThresholdSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedBloodstreamSystem _bloodstreamSystem = default!;
    [Dependency] private SharedCameraRecoilSystem _cameraSystem = default!;
    [Dependency] private SharedChatSystem _chatSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private StandingStateSystem _standingSystem = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;

    private static readonly ProtoId<ContentTileDefinition> MowedAstroGrass = "FloorMowedAstroGrass";
    private static readonly ProtoId<DecalPrototype> WheelTracks = "KsLawnmowerBloodTracks";
    private static readonly ProtoId<EmotePrototype> Scream = "Scream";
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";
    private static readonly ResPath GrassDecalSprite = new("/Textures/Decals/Flora/flora_grass.rsi");
    private static readonly TimeSpan DriveSoundInterval = TimeSpan.FromSeconds(0.8);
    private const string BladeFixture = "blades";

    [SubscribeLocalEvent]
    private void OnStartCollide(Entity<KsLawnmowerBladesComponent> entity, ref StartCollideEvent args)
    {
        if (args.OurFixtureId != BladeFixture || !HasComp<MobStateComponent>(args.OtherEntity))
            return;

        var contacts = entity.Comp.Contacts;
        contacts[args.OtherEntity] = contacts.GetValueOrDefault(args.OtherEntity) + 1;
    }

    [SubscribeLocalEvent]
    private void OnEndCollide(Entity<KsLawnmowerBladesComponent> entity, ref EndCollideEvent args)
    {
        var contacts = entity.Comp.Contacts;
        if (args.OurFixtureId != BladeFixture || !contacts.TryGetValue(args.OtherEntity, out var count))
            return;

        if (count > 1)
        {
            contacts[args.OtherEntity] = count - 1;
            return;
        }

        contacts.Remove(args.OtherEntity);
        entity.Comp.HitTargets.Remove(args.OtherEntity);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<KsLawnmowerBladesComponent, MovementRelayTargetComponent, InputMoverComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var blades, out var relay, out var mover, out var physics))
        {
            if (!mover.HasDirectionalMovement || !mover.CanMove || mover.WishDir == Vector2.Zero)
                continue;

            var entity = (uid, blades);
            var moving = physics.LinearVelocity.LengthSquared() >= 0.04f;
            if (moving)
                MowTile(entity, Transform(uid));

            // A standing target can stop the mower in the same tick that the blades detect contact.
            var crushed = _emagSystem.CheckFlag(uid, EmagType.Interaction) &&
                ProcessContacts(entity, relay.Source, mover.WishDir.Normalized());

            if (crushed)
            {
                _audioSystem.PlayPvs(blades.CrushSound, uid);
                blades.NextDriveSound = _gameTiming.CurTime + DriveSoundInterval;
            }
            else if (moving && _gameTiming.CurTime >= blades.NextDriveSound)
            {
                _audioSystem.PlayPvs(blades.DriveSound, uid);
                blades.NextDriveSound = _gameTiming.CurTime + DriveSoundInterval;
            }
        }
    }

    private void MowTile(Entity<KsLawnmowerBladesComponent> mower, TransformComponent transform)
    {
        if (transform.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid) ||
            !_mapSystem.TryGetTileRef(gridUid, grid, transform.Coordinates, out var tile) ||
            tile.Tile.IsEmpty)
        {
            mower.Comp.LastTile = null;
            return;
        }

        var tileKey = (gridUid, tile.GridIndices);
        if (mower.Comp.LastTile == tileKey)
            return;

        mower.Comp.LastTile = tileKey;
        if (mower.Comp.MowableTiles.Contains(_tileDefinitions[tile.Tile.TypeId].ID))
            _mapSystem.SetTile(gridUid, grid, tile.GridIndices, new Tile(ProtoMan.Index(MowedAstroGrass).TileId));

        var tileOrigin = new Vector2(tile.GridIndices.X, tile.GridIndices.Y) * grid.TileSize;
        var tileCenter = tileOrigin + new Vector2(grid.TileSize * 0.5f);
        foreach (var (index, decal) in _decalSystem.GetDecalsInRange(gridUid, tileCenter, distance: grid.TileSize * 0.71f))
        {
            var decalCenter = decal.Coordinates + new Vector2(0.5f);
            if (decalCenter.X < tileOrigin.X || decalCenter.X >= tileOrigin.X + grid.TileSize ||
                decalCenter.Y < tileOrigin.Y || decalCenter.Y >= tileOrigin.Y + grid.TileSize ||
                !ProtoMan.TryIndex<DecalPrototype>(decal.Id, out var prototype) ||
                prototype.Sprite is not SpriteSpecifier.Rsi sprite || sprite.RsiPath != GrassDecalSprite)
            {
                continue;
            }

            _decalSystem.RemoveDecal(gridUid, index);
        }

        if (mower.Comp.BloodyTiles <= 0)
            return;

        var rotation = _transformSystem.GetWorldRotation(transform) - _transformSystem.GetWorldRotation(gridUid);
        if (_decalSystem.TryAddDecal(WheelTracks.Id, new EntityCoordinates(gridUid, tileOrigin), out _,
            color: Color.FromHex("#8f1818").WithAlpha(mower.Comp.BloodyTiles / 4f), rotation: rotation, cleanable: true))
        {
            mower.Comp.BloodyTiles--;
        }
    }

    private bool ProcessContacts(Entity<KsLawnmowerBladesComponent> mower, EntityUid riderUid, Vector2 direction)
    {
        var crushed = false;
        var mowerPosition = _transformSystem.GetWorldPosition(mower.Owner);
        foreach (var victimUid in mower.Comp.Contacts.Keys)
        {
            if (victimUid == riderUid || mower.Comp.HitTargets.Contains(victimUid) ||
                TerminatingOrDeleted(victimUid) || EntityManager.IsQueuedForDeletion(victimUid) ||
                !TryComp<MobStateComponent>(victimUid, out var mobState))
            {
                continue;
            }

            var offset = _transformSystem.GetWorldPosition(victimUid) - mowerPosition;
            if (Vector2.Dot(direction, offset) < 0f)
                continue;

            mower.Comp.HitTargets.Add(victimUid);
            var dead = _mobStateSystem.IsDead(victimUid, mobState);
            var humanoid = HasComp<HumanoidProfileComponent>(victimUid);
            var zombie = HasComp<ZombieComponent>(victimUid);
            var prone = _standingSystem.IsDown(victimUid);

            if ((dead && !humanoid) || (zombie && (prone || dead)))
            {
                HitWithBlades(mower, victimUid, "ks-lawn-mower-gib");
                _cameraSystem.KickCamera(victimUid, direction * 0.3f);
                _gibbingSystem.Gib(victimUid, user: riderUid);
                crushed = true;
            }
            else if (!dead && humanoid && prone)
            {
                HitWithBlades(mower, victimUid, "ks-lawn-mower-crush");
                if (_mobStateSystem.IsAlive(victimUid, mobState))
                    _chatSystem.TryEmoteWithChat(victimUid, ProtoMan.Index(Scream), ignoreActionBlocker: true);

                _stunSystem.TryKnockdown(victimUid, TimeSpan.FromSeconds(10));
                ApplyCrushDamage(victimUid, riderUid, mower.Comp.CrushDamage);
                crushed = true;
            }
            else if (!dead && !prone)
            {
                _damageableSystem.TryChangeDamage(victimUid, mower.Comp.BumpDamage, ignoreResistances: true, origin: riderUid);
                _audioSystem.PlayPvs(mower.Comp.BumpSound, victimUid);
                var away = offset.LengthSquared() > 0.001f ? offset.Normalized() : direction;
                _throwingSystem.TryThrow(victimUid, away * 4f, baseThrowSpeed: 6f, recoil: false, doSpin: false, playSound: false);
            }
        }

        if (crushed)
            _cameraSystem.KickCamera(riderUid, -direction * 0.15f);

        return crushed;
    }

    private void ApplyCrushDamage(EntityUid victimUid, EntityUid riderUid, FixedPoint2 amount)
    {
        if (!TryComp<DamageableComponent>(victimUid, out var damageable))
            return;

        var currentDamage = _damageableSystem.GetPositiveDamage((victimUid, damageable));
        var totalDamage = currentDamage.GetTotal();
        if (_mobThresholdSystem.TryGetDeadThreshold(victimUid, out var lethalDamage))
            amount = FixedPoint2.Min(amount, lethalDamage.Value + 10 - totalDamage);

        // Keep the corpse intact even for species whose death and gib thresholds are close together.
        if (TryComp<DestructibleComponent>(victimUid, out var destructible))
        {
            var bluntDamage = currentDamage.DamageDict.GetValueOrDefault(Blunt);
            foreach (var threshold in destructible.Thresholds)
            {
                if (!threshold.Behaviors.Any(behavior => behavior is GibBehavior))
                    continue;

                var remaining = threshold.Trigger switch
                {
                    DamageTrigger total => total.Damage - totalDamage,
                    DamageTypeTrigger blunt when blunt.DamageType == Blunt => blunt.Damage - bluntDamage,
                    _ => FixedPoint2.MaxValue,
                };

                amount = FixedPoint2.Min(amount, remaining - FixedPoint2.Epsilon);
            }
        }

        if (amount > FixedPoint2.Zero)
        {
            var damage = new DamageSpecifier(ProtoMan.Index(Blunt), amount);
            _damageableSystem.TryChangeDamage((victimUid, damageable), damage,
                ignoreResistances: true, origin: riderUid, ignoreGlobalModifiers: true);
        }
    }

    private void HitWithBlades(Entity<KsLawnmowerBladesComponent> mower, EntityUid victimUid, LocId message)
    {
        _popupSystem.PopupEntity(KsPopupMessage.Create(message, ("mower", mower.Owner), ("victim", victimUid)),
            mower.Owner, PopupType.LargeCaution);
        _audioSystem.PlayPvs(mower.Comp.BladeSound, mower.Owner);
        if (HasComp<BloodstreamComponent>(victimUid) && _bloodstreamSystem.TryBleedOut(victimUid, 4))
            mower.Comp.BloodyTiles = 4;
    }
}
