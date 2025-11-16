using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Shared._KS14.Random.Helpers;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Administration.Logs;
using Content.Shared.Audio;
using Content.Shared.Camera;
using Content.Shared.CombatMode;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Effects;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Content.Shared.Timing;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Reflect;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _actionBlockerSystem = default!;
    [Dependency] protected readonly IGameTiming Timing = default!;
    [Dependency] protected readonly IMapManager MapManager = default!;
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] protected readonly IPrototypeManager ProtoManager = default!;
    [Dependency] protected readonly ISharedAdminLogManager Logs = default!;
    [Dependency] protected readonly DamageableSystem Damageable = default!;
    [Dependency] protected readonly ExamineSystemShared Examine = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly RechargeBasicEntityAmmoSystem _recharge = default!;
    [Dependency] protected readonly SharedActionsSystem Actions = default!;
    [Dependency] protected readonly SharedAppearanceSystem Appearance = default!;
    [Dependency] protected readonly SharedAudioSystem Audio = default!;
    [Dependency] private readonly SharedCombatModeSystem _combatMode = default!;
    [Dependency] protected readonly SharedContainerSystem Containers = default!;
    [Dependency] protected readonly SharedPointLightSystem Lights = default!;
    [Dependency] protected readonly SharedPopupSystem PopupSystem = default!;
    [Dependency] protected readonly SharedPhysicsSystem Physics = default!;
    [Dependency] protected readonly SharedProjectileSystem Projectiles = default!;
    [Dependency] protected readonly SharedTransformSystem TransformSystem = default!;
    [Dependency] protected readonly TagSystem TagSystem = default!;
    [Dependency] protected readonly ThrowingSystem ThrowingSystem = default!;
    [Dependency] private readonly UseDelaySystem _useDelay = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!; // KS14: Prediction
    [Dependency] private readonly SharedColorFlashEffectSystem _color = default!; // KS14: Prediction
    [Dependency] private readonly SharedStaminaSystem _stamina = default!; // KS14: Prediction
    [Dependency] private readonly SharedCameraRecoilSystem _recoil = default!; // KS14: Prediction

    private const float InteractNextFire = 0.3f;
    private const double SafetyNextFire = 0.5;
    private const float EjectOffset = 0.4f;
    protected const string AmmoExamineColor = "yellow";
    protected const string FireRateExamineColor = "yellow";
    public const string ModeExamineColor = "cyan";
    private const float DamagePitchVariation = 0.05f;

    public override void Initialize()
    {
        SubscribeAllEvent<RequestShootEvent>(OnShootRequest);
        SubscribeAllEvent<RequestStopShootEvent>(OnStopShootRequest);
        SubscribeLocalEvent<GunComponent, MeleeHitEvent>(OnGunMelee);

        // Ammo providers
        InitializeBallistic();
        InitializeBattery();
        InitializeCartridge();
        InitializeChamberMagazine();
        InitializeMagazine();
        InitializeRevolver();
        InitializeBasicEntity();
        InitializeClothing();
        InitializeContainer();
        InitializeSolution();

        // Interactions
        SubscribeLocalEvent<GunComponent, GetVerbsEvent<AlternativeVerb>>(OnAltVerb);
        SubscribeLocalEvent<GunComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<GunComponent, CycleModeEvent>(OnCycleMode);
        SubscribeLocalEvent<GunComponent, HandSelectedEvent>(OnGunSelected);
        SubscribeLocalEvent<GunComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<GunComponent> gun, ref MapInitEvent args)
    {
#if DEBUG
        if (gun.Comp.NextFire > Timing.CurTime)
            Log.Warning($"Initializing a map that contains an entity that is on cooldown. Entity: {ToPrettyString(gun)}");

        DebugTools.Assert((gun.Comp.AvailableModes & gun.Comp.SelectedMode) != 0x0);
#endif

        RefreshModifiers((gun, gun));
    }

    private void OnGunMelee(EntityUid uid, GunComponent component, MeleeHitEvent args)
    {
        if (!TryComp<MeleeWeaponComponent>(uid, out var melee))
            return;

        // KS14 Prediction: removed dirty
        if (melee.NextAttack > component.NextFire)
            component.NextFire = melee.NextAttack;
    }

    private void OnShootRequest(RequestShootEvent msg, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.AttachedEntity;

        // KS14: De-predict. Whose GENIUS idea was RaisePredictiveEvent?
        if (_netManager.IsClient &&
            !Timing.IsFirstTimePredicted)
            return;

        if (user == null ||
            !_combatMode.IsInCombatMode(user) ||
            !TryGetGun(user.Value, out var ent, out var gun))
        {
            return;
        }

        if (ent != GetEntity(msg.Gun))
            return;

        gun.ShootCoordinates = GetCoordinates(msg.Coordinates);
        gun.Target = GetEntity(msg.Target);
        AttemptShoot(user.Value, ent, gun);
    }

    private void OnStopShootRequest(RequestStopShootEvent ev, EntitySessionEventArgs args)
    {
        var gunUid = GetEntity(ev.Gun);

        if (args.SenderSession.AttachedEntity == null ||
            !TryComp<GunComponent>(gunUid, out var gun) ||
            !TryGetGun(args.SenderSession.AttachedEntity.Value, out _, out var userGun))
        {
            return;
        }

        if (userGun != gun)
            return;

        StopShooting(gunUid, gun);
    }

    public bool CanShoot(GunComponent component)
    {
        if (component.NextFire > Timing.CurTime)
            return false;

        return true;
    }

    public bool TryGetGun(EntityUid entity, out EntityUid gunEntity, [NotNullWhen(true)] out GunComponent? gunComp)
    {
        gunEntity = default;
        gunComp = null;

        if (_hands.GetActiveItem(entity) is { } held &&
            TryComp(held, out GunComponent? gun))
        {
            gunEntity = held;
            gunComp = gun;
            return true;
        }

        // Last resort is check if the entity itself is a gun.
        if (TryComp(entity, out gun))
        {
            gunEntity = entity;
            gunComp = gun;
            return true;
        }

        return false;
    }

    private void StopShooting(EntityUid uid, GunComponent gun)
    {
        if (gun.ShotCounter == 0)
            return;

        gun.ShotCounter = 0;
        gun.ShootCoordinates = null;
        gun.Target = null;
        gun.LastShotWasEmpty = false; // MNET
        // KS14 Prediction: removed dirty
    }

    private EntityUid PredictedSpawnAtPositionAndPredictPhysics(string? prototype, EntityCoordinates entityCoordinates)
    {
        var uid = PredictedSpawnAtPosition(prototype, entityCoordinates);

        TryComp<PredictedSpawnComponent>(uid, out var a);
        Physics.UpdateIsPredicted(uid);

        return uid;
    }

    /// <summary>
    /// Attempts to shoot at the target coordinates. Resets the shot counter after every shot.
    /// </summary>
    public bool AttemptShoot(EntityUid user, EntityUid gunUid, GunComponent gun, EntityCoordinates toCoordinates, EntityUid? target = null)
    {
        gun.ShootCoordinates = toCoordinates;
        gun.Target = target;
        var result = AttemptShoot(user, gunUid, gun);
        gun.ShotCounter = 0;
        // KS14 Prediction: removed dirty

        return result;
    }

    /// <summary>
    /// Shoots by assuming the gun is the user at default coordinates.
    /// </summary>
    public bool AttemptShoot(EntityUid gunUid, GunComponent gun)
    {
        var coordinates = new EntityCoordinates(gunUid, gun.DefaultDirection);
        gun.ShootCoordinates = coordinates;
        var result = AttemptShoot(gunUid, gunUid, gun);
        gun.ShotCounter = 0;
        return result;
    }

    private bool AttemptShoot(EntityUid user, EntityUid gunUid, GunComponent gun)
    {
        if (gun.FireRateModified <= 0f ||
            !_actionBlockerSystem.CanAttack(user))
        {
            return false;
        }

        var toCoordinates = gun.ShootCoordinates;

        if (toCoordinates == null)
            return false;

        var curTime = Timing.CurTime;

        // check if anything wants to prevent shooting
        var prevention = new ShotAttemptedEvent
        {
            User = user,
            Used = (gunUid, gun)
        };
        RaiseLocalEvent(gunUid, ref prevention);
        if (prevention.Cancelled)
            return false;

        RaiseLocalEvent(user, ref prevention);
        if (prevention.Cancelled)
            return false;

        // Need to do this to play the clicking sound for empty automatic weapons
        // but not play anything for burst fire.
        if (!CanShoot(gun))
            return false;

        var fireRate = TimeSpan.FromSeconds(1f / gun.FireRateModified);

        if (gun.SelectedMode == SelectiveFire.Burst || gun.BurstActivated)
            fireRate = TimeSpan.FromSeconds(1f / gun.BurstFireRate);

        // First shot
        // Previously we checked shotcounter but in some cases all the bullets got dumped at once
        // curTime - fireRate is insufficient because if you time it just right you can get a 3rd shot out slightly quicker.
        Log.Debug($"Our NextFire is: {gun.NextFire} and is now {curTime}");
        if (gun.NextFire < curTime - fireRate || gun.ShotCounter == 0 && gun.NextFire < curTime)
            gun.NextFire = curTime;

        var shots = 0;
        var lastFire = gun.NextFire;

        // MNET: gun.NextFire -> newNextFire
        var newNextFire = gun.NextFire;
        while (newNextFire <= curTime)
        {
            newNextFire += fireRate;
            shots++;
        }

        // Get how many shots we're actually allowed to make, due to clip size or otherwise.
        // Don't do this in the loop so we still reset NextFire.
        if (!gun.BurstActivated)
        {
            switch (gun.SelectedMode)
            {
                case SelectiveFire.SemiAuto:
                    shots = Math.Min(shots, 1 - gun.ShotCounter);
                    break;
                case SelectiveFire.Burst:
                    shots = Math.Min(shots, gun.ShotsPerBurstModified - gun.ShotCounter);
                    break;
                case SelectiveFire.FullAuto:
                    break;
                default:
                    throw new ArgumentOutOfRangeException($"No implemented shooting behavior for {gun.SelectedMode}!");
            }
        }
        else
        {
            shots = Math.Min(shots, gun.ShotsPerBurstModified - gun.ShotCounter);
        }

        var attemptEv = new AttemptShootEvent(user, null);
        RaiseLocalEvent(gunUid, ref attemptEv);

        if (attemptEv.Cancelled)
        {
            if (attemptEv.Message != null)
            {
                PopupSystem.PopupClient(attemptEv.Message, gunUid, user);
            }
            gun.BurstActivated = false;
            gun.BurstShotsCount = 0;
            gun.NextFire = TimeSpan.FromSeconds(Math.Max(lastFire.TotalSeconds + SafetyNextFire, gun.NextFire.TotalSeconds));
            //KSDBGCMNT DirtyField(gunUid, gun, nameof(GunComponent.NextFire)); // KS14 Fix

            return false;
        }

        var fromCoordinates = Transform(user).Coordinates;
        // Remove ammo
        var ev = new TakeAmmoEvent(shots, new List<(EntityUid? Entity, IShootable Shootable)>(), fromCoordinates, user);

        // Listen it just makes the other code around it easier if shots == 0 to do this.
        if (shots > 0)
            RaiseLocalEvent(gunUid, ev);

        DebugTools.Assert(ev.Ammo.Count <= shots);
        DebugTools.Assert(shots >= 0);
        UpdateAmmoCount(gunUid);

        // Even if we don't actually shoot update the ShotCounter. This is to avoid spamming empty sounds
        // where the gun may be SemiAuto or Burst.
        gun.ShotCounter += shots;
        //KSDBGCMNT DirtyField(gunUid, gun, nameof(GunComponent.ShotCounter));

        if (ev.Ammo.Count <= 0)
        {
            if (gun.LastShotWasEmpty)
                return false;

            // MNET Start
            gun.LastShotWasEmpty = true;
            gun.NextFire = lastFire + gun.EmptyFireInterval;

            //KSDBGCMNT DirtyFields(gunUid, gun, null, nameof(GunComponent.LastShotWasEmpty), nameof(GunComponent.NextFire));
            // MNET End

            // triggers effects on the gun if it's empty
            var emptyGunShotEvent = new OnEmptyGunShotEvent(user);
            RaiseLocalEvent(gunUid, ref emptyGunShotEvent);

            gun.BurstActivated = false;
            gun.BurstShotsCount = 0;
            //gun.NextFire += TimeSpan.FromSeconds(gun.BurstCooldown); // MNET

            // Play empty gun sounds if relevant
            // If they're firing an existing clip then don't play anything.
            if (shots > 0)
            {
                PopupSystem.PopupCursor(ev.Reason ?? Loc.GetString("gun-magazine-fired-empty"));

                // MNET14: ent-popup
                if (gun.EmptyFireLoc is { } emptyFireLoc)
                    PopupSystem.PopupPredicted(Loc.GetString(emptyFireLoc), user, user);

                // Don't spam safety sounds at gun fire rate, play it at a reduced rate.
                // May cause prediction issues? Needs more tweaking

                // MNET14: commented out
                // gun.NextFire = TimeSpan.FromSeconds(Math.Max(lastFire.TotalSeconds + SafetyNextFire, gun.NextFire.TotalSeconds));

                Audio.PlayPredicted(gun.SoundEmpty, gunUid, user);
                return false;
            }

            return false;
        }

        // MNET Start
        gun.LastShotWasEmpty = false;

        // TODO: explain why this is being done
        var ammoEv = new GetAmmoCountEvent();
        RaiseLocalEvent(gunUid, ref ammoEv);

        // if we have no ammo then don't update NextFire, so we can do empty-fire effects immediately after running out of ammo, instead of waiting
        if (ammoEv.Count > 0)
            gun.NextFire = newNextFire;

        Log.Debug($"NextFire is now {newNextFire}");
        // NextFire has been touched regardless so need to dirty the gun.
        //KSDBGCMNT DirtyFields(gunUid, gun, null, nameof(GunComponent.LastShotWasEmpty), nameof(GunComponent.NextFire));
        // MNET End

        // Handle burstfire
        if (gun.SelectedMode == SelectiveFire.Burst)
        {
            gun.BurstActivated = true;
        }
        if (gun.BurstActivated)
        {
            gun.BurstShotsCount += shots;
            if (gun.BurstShotsCount >= gun.ShotsPerBurstModified)
            {
                gun.NextFire += TimeSpan.FromSeconds(gun.BurstCooldown);
                gun.BurstActivated = false;
                gun.BurstShotsCount = 0;
            }
        }

        // Shoot confirmed - sounds also played here in case it's invalid (e.g. cartridge already spent).
        Shoot(gunUid, gun, ev.Ammo, fromCoordinates, toCoordinates.Value, out var userImpulse, user, throwItems: attemptEv.ThrowItems);
        var shotEv = new GunShotEvent(user, ev.Ammo);
        RaiseLocalEvent(gunUid, ref shotEv);

        if (!userImpulse || !TryComp<PhysicsComponent>(user, out var userPhysics))
            return true;

        var shooterEv = new ShooterImpulseEvent();
        RaiseLocalEvent(user, ref shooterEv);

        if (shooterEv.Push)
            CauseImpulse(fromCoordinates, toCoordinates.Value, user, userPhysics);
        return true;
    }

    public void Shoot(
        EntityUid gunUid,
        GunComponent gun,
        EntityUid ammo,
        EntityCoordinates fromCoordinates,
        EntityCoordinates toCoordinates,
        out bool userImpulse,
        EntityUid? user = null,
        bool throwItems = false)
    {
        var shootable = EnsureShootable(ammo);
        Shoot(gunUid, gun, new List<(EntityUid? Entity, IShootable Shootable)>(1) { (ammo, shootable) }, fromCoordinates, toCoordinates, out userImpulse, user, throwItems);
    }

    // KS14: Predicted; fully moved to shared.
    private static Angle GetRecoilAngle(TimeSpan curTime, GunComponent component, Angle direction, System.Random predictedRandom)
    {
        var timeSinceLastFire = (curTime - component.LastFire).TotalSeconds;
        var newTheta = MathHelper.Clamp(component.CurrentAngle.Theta + component.AngleIncreaseModified.Theta - component.AngleDecayModified.Theta * timeSinceLastFire, component.MinAngleModified.Theta, component.MaxAngleModified.Theta);
        component.CurrentAngle = new Angle(newTheta);
        component.LastFire = component.NextFire;

        // Convert it so angle can go either side.
        var random = predictedRandom.NextFloat(-0.5f, 0.5f);
        var spread = component.CurrentAngle.Theta * random;
        var angle = new Angle(direction.Theta + component.CurrentAngle.Theta * random);
        DebugTools.Assert(spread <= component.MaxAngleModified.Theta);
        return angle;
    }

    // KS14: Predicted; fully moved to shared.
    private void ShootOrThrow(EntityUid uid, Vector2 mapDirection, Vector2 gunVelocity, GunComponent gun, EntityUid gunUid, EntityUid? user)
    {
        if (gun.Target is { } target && !TerminatingOrDeleted(target))
        {
            var targeted = EnsureComp<TargetedProjectileComponent>(uid);
            targeted.Target = target;
            // KS14 Prediction: removed dirty
        }

        // Do a throw
        if (!HasComp<ProjectileComponent>(uid))
        {
            RemoveShootable(uid);
            // TODO: Someone can probably yeet this a billion miles so need to pre-validate input somewhere up the call stack.
            // KS14 NOTE: this TODO probably still applies ^
            ThrowingSystem.TryThrow(uid, mapDirection, gun.ProjectileSpeedModified, user);
            return;
        }

        ShootProjectile(uid, mapDirection, gunVelocity, gunUid, user, gun.ProjectileSpeedModified);
    }

    // KS14: Predicted; fully moved to shared.
    /// <summary>
    /// Gets a linear spread of angles between start and end.
    /// </summary>
    /// <param name="start">Start angle in degrees</param>
    /// <param name="end">End angle in degrees</param>
    /// <param name="intervals">How many shots there are</param>
    private static Angle[] LinearSpread(Angle start, Angle end, int intervals)
    {
        var angles = new Angle[intervals];
        DebugTools.Assert(intervals > 1);

        for (var i = 0; i <= intervals - 1; i++)
        {
            angles[i] = new Angle(start + (end - start) * i / (intervals - 1));
        }

        return angles;
    }

    // KS14: Predicted; fully moved to shared.
    private void FireEffects(EntityCoordinates fromCoordinates, float distance, Angle angle, HitscanPrototype hitscan, EntityUid? hitEntity = null, Entity<MetaDataComponent?>? user = null)
    {
        // Lord
        // Forgive me for the shitcode I am about to do
        // Effects tempt me not
        var sprites = new List<(NetCoordinates coordinates, Angle angle, SpriteSpecifier sprite, float scale)>();
        var fromXform = Transform(fromCoordinates.EntityId);

        // We'll get the effects relative to the grid / map of the firer
        // Look you could probably optimise this a bit with redundant transforms at this point.

        var gridUid = fromXform.GridUid;
        if (gridUid != fromCoordinates.EntityId && TryComp(gridUid, out TransformComponent? gridXform))
        {
            var (_, gridRot, gridInvMatrix) = TransformSystem.GetWorldPositionRotationInvMatrix(gridXform);
            var map = TransformSystem.ToMapCoordinates(fromCoordinates);
            fromCoordinates = new EntityCoordinates(gridUid.Value, Vector2.Transform(map.Position, gridInvMatrix));
            angle -= gridRot;
        }
        else
        {
            angle -= TransformSystem.GetWorldRotation(fromXform);
        }

        if (distance >= 1f)
        {
            if (hitscan.MuzzleFlash != null)
            {
                var coords = fromCoordinates.Offset(angle.ToVec().Normalized() / 2);
                var netCoords = GetNetCoordinates(coords);

                sprites.Add((netCoords, angle, hitscan.MuzzleFlash, 1f));
            }

            if (hitscan.TravelFlash != null)
            {
                var coords = fromCoordinates.Offset(angle.ToVec() * (distance + 0.5f) / 2);
                var netCoords = GetNetCoordinates(coords);

                sprites.Add((netCoords, angle, hitscan.TravelFlash, distance - 1.5f));
            }
        }

        if (hitscan.ImpactFlash != null)
        {
            var coords = fromCoordinates.Offset(angle.ToVec() * distance);
            var netCoords = GetNetCoordinates(coords);

            sprites.Add((netCoords, angle.FlipPositive(), hitscan.ImpactFlash, 1f));
        }

        if (sprites.Count > 0)
        {
            if (_netManager.IsServer)
                RaiseNetworkEvent(new HitscanEvent { Sprites = sprites, User = GetNetEntity(user) }, Filter.Pvs(fromCoordinates, entityMan: EntityManager));

            RaiseLocalEvent(new HitscanEvent { Sprites = sprites });
        }
    }

    // KS14: Predicted; fully moved to shared.
    public void PlayImpactSound(EntityUid otherEntity, DamageSpecifier? modifiedDamage, SoundSpecifier? weaponSound, bool forceWeaponSound)
    {
        DebugTools.Assert(!Deleted(otherEntity), "Impact sound entity was deleted");

        // Like projectiles and melee,
        // 1. Entity specific sound
        // 2. Ammo's sound
        // 3. Nothing
        var playedSound = false;

        if (!forceWeaponSound && modifiedDamage != null && modifiedDamage.GetTotal() > 0 && TryComp<RangedDamageSoundComponent>(otherEntity, out var rangedSound))
        {
            var type = SharedMeleeWeaponSystem.GetHighestDamageSound(modifiedDamage, ProtoManager);

            if (type != null && rangedSound.SoundTypes?.TryGetValue(type, out var damageSoundType) == true)
            {
                Audio.PlayPvs(damageSoundType, otherEntity, AudioParams.Default.WithVariation(DamagePitchVariation));
                playedSound = true;
            }
            else if (type != null && rangedSound.SoundGroups?.TryGetValue(type, out var damageSoundGroup) == true)
            {
                Audio.PlayPvs(damageSoundGroup, otherEntity, AudioParams.Default.WithVariation(DamagePitchVariation));
                playedSound = true;
            }
        }

        if (!playedSound && weaponSound != null)
        {
            Audio.PlayPvs(weaponSound, otherEntity);
        }
    }

    // KS14: Predicted; now declared in shared.
    private void Recoil(EntityUid? user, Vector2 recoil, float recoilScalar)
    {
        if (!Timing.IsFirstTimePredicted || user == null || recoil == Vector2.Zero || recoilScalar == 0)
            return;

        _recoil.KickCamera(user.Value, recoil.Normalized() * 0.5f * recoilScalar);
    }

    // KS14: Predicted; fully moved to shared.
    public void Shoot(
        EntityUid gunUid,
        GunComponent gun,
        List<(EntityUid? Entity, IShootable Shootable)> ammo,
        EntityCoordinates fromCoordinates,
        EntityCoordinates toCoordinates,
        out bool userImpulse,
        EntityUid? user = null,
        bool throwItems = false)
    {
        userImpulse = true;

        if (user != null)
        {
            var selfEvent = new SelfBeforeGunShotEvent(user.Value, (gunUid, gun), ammo);
            RaiseLocalEvent(user.Value, selfEvent);
            if (selfEvent.Cancelled)
            {
                userImpulse = false;
                return;
            }
        }

        var predictedRandom =
            KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(KsSharedRandomExtensions.GetNetId(gunUid, EntityManager), (int)Timing.CurTick.Value);

        var fromMap = TransformSystem.ToMapCoordinates(fromCoordinates);
        var toMap = TransformSystem.ToMapCoordinates(toCoordinates).Position;
        var mapDirection = toMap - fromMap.Position;
        var mapAngle = mapDirection.ToAngle();
        var angle = GetRecoilAngle(Timing.CurTime, gun, mapDirection.ToAngle(), predictedRandom);

        // If applicable, this ensures the projectile is parented to grid on spawn, instead of the map.
        var fromEnt = MapManager.TryFindGridAt(fromMap, out var gridUid, out _)
            ? TransformSystem.WithEntityId(fromCoordinates, gridUid)
            : new EntityCoordinates(_mapSystem.GetMapOrInvalid(fromMap.MapId), fromMap.Position);

        // Update shot based on the recoil
        toMap = fromMap.Position + angle.ToVec() * mapDirection.Length();
        mapDirection = toMap - fromMap.Position;
        var gunVelocity = Physics.GetMapLinearVelocity(fromEnt);

        // I must be high because this was getting tripped even when true.
        // DebugTools.Assert(direction != Vector2.Zero);
        var shotProjectiles = new List<EntityUid>(ammo.Count);

        foreach (var (ent, shootable) in ammo)
        {
            // pneumatic cannon doesn't shoot bullets it just throws them, ignore ammo handling
            if (throwItems && ent != null)
            {
                ShootOrThrow(ent.Value, mapDirection, gunVelocity, gun, gunUid, user);
                continue;
            }

            switch (shootable)
            {
                // Cartridge shoots something else
                case CartridgeAmmoComponent cartridge:
                    if (!cartridge.Spent)
                    {
                        var uid = PredictedSpawnAtPositionAndPredictPhysics(cartridge.Prototype, fromEnt);
                        CreateAndFireProjectiles(uid, cartridge);

                        RaiseLocalEvent(ent!.Value, new AmmoShotEvent()
                        {
                            FiredProjectiles = shotProjectiles,
                        });

                        SetCartridgeSpent(ent!.Value, cartridge, true);

                        if (cartridge.DeleteOnSpawn)
                            PredictedDel(ent.Value);
                    }
                    else
                    {
                        userImpulse = false;
                        Audio.PlayPredicted(gun.SoundEmpty, gunUid, user);
                    }

                    // Something like ballistic might want to leave it in the container still
                    if (!cartridge.DeleteOnSpawn && !Containers.IsEntityInContainer(ent!.Value))
                        EjectCartridge(ent.Value, angle, user: user);

                    // KS14 Prediction: removed dirty
                    break;
                // Ammo shoots itself
                case AmmoComponent newAmmo:
                    if (ent == null)
                        break;
                    CreateAndFireProjectiles(ent.Value, newAmmo);

                    break;
                case HitscanPrototype hitscan:

                    EntityUid? lastHit = null;

                    var from = fromMap;
                    // can't use map coords above because funny FireEffects
                    var fromEffect = fromCoordinates;
                    var dir = mapDirection.Normalized();

                    //in the situation when user == null, means that the cannon fires on its own (via signals). And we need the gun to not fire by itself in this case
                    var lastUser = user ?? gunUid;

                    if (hitscan.Reflective != ReflectType.None)
                    {
                        for (var reflectAttempt = 0; reflectAttempt < 3; reflectAttempt++)
                        {
                            var ray = new CollisionRay(from.Position, dir, hitscan.CollisionMask);
                            var rayCastResults =
                                Physics.IntersectRay(from.MapId, ray, hitscan.MaxLength, lastUser, false).ToList();
                            if (!rayCastResults.Any())
                                break;

                            var result = rayCastResults[0];

                            // Check if laser is shot from in a container
                            if (!Containers.IsEntityOrParentInContainer(lastUser))
                            {
                                // Checks if the laser should pass over unless targeted by its user
                                foreach (var collide in rayCastResults)
                                {
                                    if (collide.HitEntity != gun.Target &&
                                        CompOrNull<RequireProjectileTargetComponent>(collide.HitEntity)?.Active == true)
                                    {
                                        continue;
                                    }

                                    result = collide;
                                    break;
                                }
                            }

                            var hit = result.HitEntity;
                            lastHit = hit;

                            FireEffects(fromEffect, result.Distance, dir.Normalized().ToAngle(), hitscan, hit, user: user);

                            var ev = new HitScanReflectAttemptEvent(user, gunUid, hitscan.Reflective, dir, false);
                            RaiseLocalEvent(hit, ref ev);

                            if (!ev.Reflected)
                                break;

                            fromEffect = Transform(hit).Coordinates;
                            from = TransformSystem.ToMapCoordinates(fromEffect);
                            dir = ev.Direction;
                            lastUser = hit;
                        }
                    }

                    if (lastHit != null)
                    {
                        var hitEntity = lastHit.Value;
                        if (hitscan.StaminaDamage > 0f)
                            _stamina.TakeStaminaDamage(hitEntity, hitscan.StaminaDamage, source: user);

                        var dmg = hitscan.Damage;

                        var hitName = ToPrettyString(hitEntity);
                        if (dmg != null)
                            dmg = Damageable.TryChangeDamage(hitEntity, dmg * Damageable.UniversalHitscanDamageModifier, origin: user);

                        // check null again, as TryChangeDamage returns modified damage values
                        if (dmg != null)
                        {
                            if (!Deleted(hitEntity))
                            {
                                if (dmg.AnyPositive())
                                {
                                    _color.RaiseEffect(Color.Red, new List<EntityUid>() { hitEntity }, Filter.Pvs(hitEntity, entityManager: EntityManager));
                                }

                                // TODO get fallback position for playing hit sound.
                                PlayImpactSound(hitEntity, dmg, hitscan.Sound, hitscan.ForceSound);
                            }

                            if (user != null)
                            {
                                Logs.Add(LogType.HitScanHit,
                                    $"{ToPrettyString(user.Value):user} hit {hitName:target} using hitscan and dealt {dmg.GetTotal():damage} damage");
                            }
                            else
                            {
                                Logs.Add(LogType.HitScanHit,
                                    $"{hitName:target} hit by hitscan dealing {dmg.GetTotal():damage} damage");
                            }
                        }
                    }
                    else
                    {
                        FireEffects(fromEffect, hitscan.MaxLength, dir.ToAngle(), hitscan, user: user);
                    }

                    Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, user);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        RaiseLocalEvent(gunUid, new AmmoShotEvent()
        {
            FiredProjectiles = shotProjectiles,
        });

        void CreateAndFireProjectiles(EntityUid ammoEnt, AmmoComponent ammoComp)
        {
            if (TryComp<ProjectileSpreadComponent>(ammoEnt, out var ammoSpreadComp))
            {
                var spreadEvent = new GunGetAmmoSpreadEvent(ammoSpreadComp.Spread);
                RaiseLocalEvent(gunUid, ref spreadEvent);

                var angles = LinearSpread(mapAngle - spreadEvent.Spread / 2,
                    mapAngle + spreadEvent.Spread / 2, ammoSpreadComp.Count);

                ShootOrThrow(ammoEnt, angles[0].ToVec(), gunVelocity, gun, gunUid, user);
                shotProjectiles.Add(ammoEnt);

                for (var i = 1; i < ammoSpreadComp.Count; i++)
                {
                    var newuid = PredictedSpawnAtPositionAndPredictPhysics(ammoSpreadComp.Proto, fromEnt); // KS14: predictedspawn
                    ShootOrThrow(newuid, angles[i].ToVec(), gunVelocity, gun, gunUid, user);
                    shotProjectiles.Add(newuid);
                }
            }
            else
            {
                ShootOrThrow(ammoEnt, mapDirection, gunVelocity, gun, gunUid, user);
                shotProjectiles.Add(ammoEnt);
            }

            MuzzleFlash(gunUid, ammoComp, mapDirection.ToAngle(), user);
            Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, user);
            Recoil(user, mapDirection, gun.CameraRecoilScalarModified); // KS14: Recoil
        }
    }

    public void ShootProjectile(EntityUid uid, Vector2 direction, Vector2 gunVelocity, EntityUid? gunUid, EntityUid? user = null, float speed = 20f)
    {
        var physics = EnsureComp<PhysicsComponent>(uid);
        Physics.SetBodyStatus(uid, physics, BodyStatus.InAir);

        var targetMapVelocity = gunVelocity + direction.Normalized() * speed;
        var currentMapVelocity = Physics.GetMapLinearVelocity(uid, physics);
        var finalLinear = physics.LinearVelocity + targetMapVelocity - currentMapVelocity;
        Physics.SetLinearVelocity(uid, finalLinear, dirty: true, body: physics);

        var projectile = EnsureComp<ProjectileComponent>(uid);
        projectile.Weapon = gunUid;
        var shooter = user ?? gunUid;
        if (shooter != null)
            Projectiles.SetShooter(uid, projectile, shooter.Value);

        TransformSystem.SetWorldRotation(uid, direction.ToWorldAngle() + projectile.Angle);
    }

    protected abstract void Popup(string message, EntityUid? uid, EntityUid? user);

    /// <summary>
    /// Call this whenever the ammo count for a gun changes.
    /// </summary>
    protected virtual void UpdateAmmoCount(EntityUid uid, bool prediction = true) { }

    protected void SetCartridgeSpent(EntityUid uid, CartridgeAmmoComponent cartridge, bool spent)
    {
        if (cartridge.Spent != spent)
            DirtyField(uid, cartridge, nameof(CartridgeAmmoComponent.Spent));

        cartridge.Spent = spent;
        Appearance.SetData(uid, AmmoVisuals.Spent, spent);
    }

    /// <summary>
    /// Drops a single cartridge / shell
    /// </summary>
    protected void EjectCartridge(
        EntityUid entity,
        Angle? angle = null,
        bool playSound = true,
        System.Random? predictedRandom = null, // KS14: predictedrandom
        EntityUid? user = null
    )
    {
        // TODO: Sound limit version.

        // KS14: Predicted random
        predictedRandom ??= KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(KsSharedRandomExtensions.GetNetId(entity, EntityManager), (int)Timing.CurTick.Value, (int?)angle?.Theta ?? 0);

        var offsetPos = predictedRandom.NextVector2(EjectOffset);
        var xform = Transform(entity);

        var coordinates = xform.Coordinates;
        coordinates = coordinates.Offset(offsetPos);

        TransformSystem.SetLocalRotation(entity, predictedRandom.NextAngle(), xform);
        TransformSystem.SetCoordinates(entity, xform, coordinates);

        // decides direction the casing ejects and only when not cycling
        if (angle is { } ejectAngle)
        {
            ejectAngle += 3.7f; // 212 degrees; casings should eject slightly to the right and behind of a gun

            Log.Debug($"Logging EjectCartridge: user: {ToPrettyString(user)}");
            ThrowingSystem.TryThrow(entity, ejectAngle.ToVec().Normalized() / 100, 5f, user: user, recoil: false);
        }

        // KS14: Predicted audio
        if (playSound && TryComp<CartridgeAmmoComponent>(entity, out var cartridge))
            Audio.PlayPredicted(cartridge.EjectSound, entity, user, audioParams: AudioParams.Default.WithVariation(SharedContentAudioSystem.DefaultVariation).WithVolume(-1f));
    }

    protected IShootable EnsureShootable(EntityUid uid)
    {
        if (TryComp<CartridgeAmmoComponent>(uid, out var cartridge))
            return cartridge;

        return EnsureComp<AmmoComponent>(uid);
    }

    protected void RemoveShootable(EntityUid uid)
    {
        RemCompDeferred<CartridgeAmmoComponent>(uid);
        RemCompDeferred<AmmoComponent>(uid);
    }

    protected void MuzzleFlash(EntityUid gun, AmmoComponent component, Angle worldAngle, EntityUid? user = null)
    {
        var attemptEv = new GunMuzzleFlashAttemptEvent();
        RaiseLocalEvent(gun, ref attemptEv);
        if (attemptEv.Cancelled)
            return;

        var sprite = component.MuzzleFlash;

        if (sprite == null)
            return;

        var ev = new MuzzleFlashEvent(GetNetEntity(gun), sprite, worldAngle);
        CreateEffect(gun, ev, user);
    }

    public void CauseImpulse(EntityCoordinates fromCoordinates, EntityCoordinates toCoordinates, EntityUid user, PhysicsComponent userPhysics)
    {
        var fromMap = TransformSystem.ToMapCoordinates(fromCoordinates).Position;
        var toMap = TransformSystem.ToMapCoordinates(toCoordinates).Position;
        var shotDirection = (toMap - fromMap).Normalized();

        const float impulseStrength = 25.0f;
        var impulseVector = shotDirection * impulseStrength;
        Physics.ApplyLinearImpulse(user, -impulseVector, body: userPhysics);
    }

    public void RefreshModifiers(Entity<GunComponent?> gun)
    {
        if (!Resolve(gun, ref gun.Comp))
            return;

        var comp = gun.Comp;
        var ev = new GunRefreshModifiersEvent(
            (gun, comp),
            comp.SoundGunshot,
            comp.CameraRecoilScalar,
            comp.AngleIncrease,
            comp.AngleDecay,
            comp.MaxAngle,
            comp.MinAngle,
            comp.ShotsPerBurst,
            comp.FireRate,
            comp.ProjectileSpeed
        );

        RaiseLocalEvent(gun, ref ev);

        if (comp.SoundGunshotModified != ev.SoundGunshot)
        {
            comp.SoundGunshotModified = ev.SoundGunshot;
            DirtyField(gun, nameof(GunComponent.SoundGunshotModified));
        }

        if (!MathHelper.CloseTo(comp.CameraRecoilScalarModified, ev.CameraRecoilScalar))
        {
            comp.CameraRecoilScalarModified = ev.CameraRecoilScalar;
            DirtyField(gun, nameof(GunComponent.CameraRecoilScalarModified));
        }

        if (!comp.AngleIncreaseModified.EqualsApprox(ev.AngleIncrease))
        {
            comp.AngleIncreaseModified = ev.AngleIncrease;
            DirtyField(gun, nameof(GunComponent.AngleIncreaseModified));
        }

        if (!comp.AngleDecayModified.EqualsApprox(ev.AngleDecay))
        {
            comp.AngleDecayModified = ev.AngleDecay;
            DirtyField(gun, nameof(GunComponent.AngleDecayModified));
        }

        if (!comp.MaxAngleModified.EqualsApprox(ev.MaxAngle))
        {
            comp.MaxAngleModified = ev.MaxAngle;
            DirtyField(gun, nameof(GunComponent.MaxAngleModified));
        }

        if (!comp.MinAngleModified.EqualsApprox(ev.MinAngle))
        {
            comp.MinAngleModified = ev.MinAngle;
            DirtyField(gun, nameof(GunComponent.MinAngleModified));
        }

        if (comp.ShotsPerBurstModified != ev.ShotsPerBurst)
        {
            comp.ShotsPerBurstModified = ev.ShotsPerBurst;
            DirtyField(gun, nameof(GunComponent.ShotsPerBurstModified));
        }

        if (!MathHelper.CloseTo(comp.FireRateModified, ev.FireRate))
        {
            comp.FireRateModified = ev.FireRate;
            DirtyField(gun, nameof(GunComponent.FireRateModified));
        }

        if (!MathHelper.CloseTo(comp.ProjectileSpeedModified, ev.ProjectileSpeed))
        {
            comp.ProjectileSpeedModified = ev.ProjectileSpeed;
            DirtyField(gun, nameof(GunComponent.ProjectileSpeedModified));
        }
    }

    protected abstract void CreateEffect(EntityUid gunUid, MuzzleFlashEvent message, EntityUid? user = null);

    /// <summary>
    /// Used for animated effects on the client.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class HitscanEvent : EntityEventArgs
    {
        public List<(NetCoordinates coordinates, Angle angle, SpriteSpecifier Sprite, float Distance)> Sprites = new();

        // KS14: Added user
        public NetEntity? User;
    }
}

/// <summary>
///     Raised directed on the gun before firing to see if the shot should go through.
/// </summary>
/// <remarks>
///     Handling this in server exclusively will lead to mispredicts.
/// </remarks>
/// <param name="User">The user that attempted to fire this gun.</param>
/// <param name="Cancelled">Set this to true if the shot should be cancelled.</param>
/// <param name="ThrowItems">Set this to true if the ammo shouldn't actually be fired, just thrown.</param>
[ByRefEvent]
public record struct AttemptShootEvent(EntityUid User, string? Message, bool Cancelled = false, bool ThrowItems = false);

/// <summary>
///     Raised directed on the gun after firing.
/// </summary>
/// <param name="User">The user that fired this gun.</param>
[ByRefEvent]
public record struct GunShotEvent(EntityUid User, List<(EntityUid? Uid, IShootable Shootable)> Ammo);

/// <summary>
/// Raised on an entity after firing a gun to see if any components or systems would allow this entity to be pushed
/// by the gun they're firing. If true, GunSystem will create an impulse on our entity.
/// </summary>
[ByRefEvent]
public record struct ShooterImpulseEvent()
{
    public bool Push;
};

public enum EffectLayers : byte
{
    Unshaded,
}

[Serializable, NetSerializable]
public enum AmmoVisuals : byte
{
    Spent,
    AmmoCount,
    AmmoMax,
    HasAmmo, // used for generic visualizers. c# stuff can just check ammocount != 0
    MagLoaded,
    BoltClosed,
}
