using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared._Trauma.Hands;
using Content.Shared.ActionBlocker;
using Content.Shared.Bed.Sleep;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Random.Helpers;
using Content.Shared.Standing;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Weapons.Ranged;

/// <summary>
///     Going down while firing a full-auto gun can clench the shooter's hand on it: they hold on, and it keeps firing
///         until it runs dry or they recover, its aim walked sideways by recoil.
/// </summary>
/// <remarks>
///     Firing is predicted for the shooter, rather than being server-only. The server leaves them out of a shot's sound
///         and muzzle flash, expecting their client to have played its own.
/// </remarks>
public sealed partial class KsTriggerSpasmSystem : EntitySystem
{
    /// <summary>
    ///     How long after a gun was next due to fire its trigger still counts as held. Shoot requests arrive with some
    ///         jitter, so a held trigger can be slightly late.
    /// </summary>
    private static readonly TimeSpan TriggerHeldGrace = TimeSpan.FromSeconds(0.2);

    /// <summary>
    ///     The lowest the least recoil can move the aim by per shot. Each shot rolls that least between this and
    ///         <see cref="HighestDriftFloor"/>, as many guns have too little angle increase of their own to notice.
    /// </summary>
    public static readonly Angle LowestDriftFloor = Angle.FromDegrees(2);

    /// <summary>
    ///     The highest the least recoil can move the aim by per shot.
    /// </summary>
    /// <seealso cref="LowestDriftFloor"/>
    public static readonly Angle HighestDriftFloor = Angle.FromDegrees(6);

    private const float MinimumDriftScale = 0.5f;
    private const float MaximumDriftScale = 1.5f;

    /// <summary>
    ///     How far ahead of the user a shot is aimed. Only the direction matters.
    /// </summary>
    private const float AimDistance = 10f;

    [Dependency] private ActionBlockerSystem _actionBlockerSystem = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedGunSystem _gunSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<GunComponent> _gunQuery = default!;
    [Dependency] private EntityQuery<KsTriggerSpasmComponent> _triggerSpasmQuery = default!;

    private float _chance;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.GunTriggerSpasmChance, value => _chance = value, true);
    }

    // Before the hands empty, as that's the last chance to see what they were holding. Stuns, crit and death all end up here.
    [SubscribeLocalEvent(before: [typeof(PredictedHandsSystem)])]
    private void OnDropHandItems(Entity<MobStateComponent> entity, ref DropHandItemsEvent args)
    {
        TryStart(entity.Owner);
    }

    // Falling asleep doesn't drop anything, so it has to be caught separately. After SleepingSystem, which stuns the sleeper.
    [SubscribeLocalEvent(after: [typeof(SleepingSystem)])]
    private void OnSleepStateChanged(Entity<HandsComponent> entity, ref SleepStateChangedEvent args)
    {
        if (args.FellAsleep)
            TryStart(entity.Owner);
    }

    private void TryStart(EntityUid userUid)
    {
        if (_gameTiming.ApplyingState)
            return;

        if (_handsSystem.GetActiveItem(userUid) is not { } gunUid ||
            _triggerSpasmQuery.HasComp(gunUid) ||
            !_gunQuery.TryComp(gunUid, out var gunComponent) ||
            !IsTriggerHeld(gunComponent) ||
            gunComponent.ShootCoordinates is not { } shootCoordinates ||
            !_transformSystem.IsValid(shootCoordinates))
            return;

        // Whoever can still shoot, such as someone only knocked down, hasn't lost control of their hand.
        if (_actionBlockerSystem.CanAttack(userUid))
            return;

        var gunNetEntity = GetNetEntity(gunUid);
        var userNetEntity = GetNetEntity(userUid);
        if (!SharedRandomExtensions.PredictedProb(_gameTiming, _chance, userNetEntity, gunNetEntity))
            return;

        var userPosition = _transformSystem.GetMapCoordinates(userUid).Position;
        var targetPosition = _transformSystem.ToMapCoordinates(shootCoordinates).Position;
        var random = SharedRandomExtensions.PredictedRandom(_gameTiming, gunNetEntity, userNetEntity);

        var triggerSpasmComponent = EnsureComp<KsTriggerSpasmComponent>(gunUid);
        triggerSpasmComponent.User = userUid;
        triggerSpasmComponent.AimAngle = (targetPosition - userPosition).ToAngle();
        triggerSpasmComponent.DriftSign = random.Prob(0.5f) ? 1 : -1;
        Dirty(gunUid, triggerSpasmComponent);

        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-trigger-spasm-start"), userUid, userUid, type: PopupType.MediumCaution);
    }

    /// <remarks>
    ///     Not <see cref="GunComponent.ShotCounter"/>: it's reset after every shot for NPCs and hold-to-attack clients.
    ///         This holds for both, and for anyone else, letting go of the trigger clears the gun's coordinates, which
    ///         <see cref="TryStart"/> checks too.
    /// </remarks>
    private bool IsTriggerHeld(GunComponent gunComponent)
    {
        return gunComponent.SelectedMode == SelectiveFire.FullAuto &&
            !gunComponent.LastShotWasEmpty &&
            _gameTiming.CurTime <= gunComponent.NextFire + TriggerHeldGrace;
    }

    // Stuns and the step from crit to death raise the drop again, and it shouldn't get a fresh roll against the grip.
    [SubscribeLocalEvent]
    private void OnGunFellDown(Entity<KsTriggerSpasmComponent> entity, ref FellDownThrowAttemptEvent args)
    {
        args.Cancelled = true;
    }

    // A wielded gun's other hand holds a virtual item, and dropping that would unwield the gun.
    [SubscribeLocalEvent]
    private void OnVirtualItemFellDown(Entity<VirtualItemComponent> entity, ref FellDownThrowAttemptEvent args)
    {
        if (_triggerSpasmQuery.HasComp(entity.Comp.BlockingEntity))
            args.Cancelled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        var query = EntityQueryEnumerator<KsTriggerSpasmComponent, GunComponent>();
        while (query.MoveNext(out var gunUid, out var triggerSpasmComponent, out var gunComponent))
        {
            // A client only predicts its own gun.
            if (_netManager.IsClient && _playerManager.LocalEntity != triggerSpasmComponent.User)
                continue;

            var gun = new Entity<GunComponent>(gunUid, gunComponent);
            if (ShouldLetGo(triggerSpasmComponent.User, gunUid))
            {
                Stop((gunUid, triggerSpasmComponent), gun);
                continue;
            }

            if (gunComponent.NextFire > curTime)
                continue;

            // Failing here means an empty click, or something else stopping the gun for good. A corpse never recovers,
            //      so waiting for that to change could take forever.
            if (!Fire((gunUid, triggerSpasmComponent), gun))
                Stop((gunUid, triggerSpasmComponent), gun);
        }
    }

    private bool ShouldLetGo(EntityUid userUid, EntityUid gunUid)
    {
        return TerminatingOrDeleted(userUid) ||
            !_handsSystem.IsHolding(userUid, gunUid) ||
            _containerSystem.IsEntityInContainer(userUid) ||
            _actionBlockerSystem.CanAttack(userUid);
    }

    private bool Fire(Entity<KsTriggerSpasmComponent> entity, Entity<GunComponent> gun)
    {
        var userUid = entity.Comp.User;
        var userCoordinates = _transformSystem.GetMapCoordinates(userUid);
        var targetCoordinates = new MapCoordinates(
            userCoordinates.Position + entity.Comp.AimAngle.ToVec() * AimDistance,
            userCoordinates.MapId);

        if (!_gunSystem.KsAttemptInvoluntaryShoot(userUid, gun, _transformSystem.ToCoordinates(userUid, targetCoordinates)))
            return false;

        var random = SharedRandomExtensions.PredictedRandom(_gameTiming, GetNetEntity(gun));
        var drift = Math.Max(
            Math.Abs(gun.Comp.AngleIncreaseModified.Theta) * random.NextFloat(MinimumDriftScale, MaximumDriftScale),
            random.NextFloat((float)LowestDriftFloor.Theta, (float)HighestDriftFloor.Theta));

        entity.Comp.AimAngle = new Angle(entity.Comp.AimAngle.Theta + entity.Comp.DriftSign * drift);
        Dirty(entity);
        return true;
    }

    private void Stop(Entity<KsTriggerSpasmComponent> entity, Entity<GunComponent> gun)
    {
        RemCompDeferred(entity.Owner, entity.Comp);
        _gunSystem.KsReleaseTrigger(gun);
    }
}
