using Content.Shared._KS14.Power.PTL;
using Content.Shared.Flash;
using Content.Server.Power.SMES;
using Content.Server.Stack;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Emag.Systems;
using Content.Shared.Radiation.Components;
using Content.Shared.Radiation.Systems;
using Content.Shared.Stacks;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Power.Components;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using System.Numerics;

namespace Content.Server._KS14.Power.PTL;

public sealed partial class PtlSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly GunSystem _gunSystem = default!;
    [Dependency] private readonly SharedFlashSystem _flashSystem = default!;
    [Dependency] private readonly StackSystem _stackSystem = default!;
    [Dependency] private readonly AudioSystem _audioSystem = default!;
    [Dependency] private readonly EmagSystem _emagSystem = default!;
    [Dependency] private readonly UserInterfaceSystem _userInterfaceSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedBatterySystem _batterySystem = default!;
    [Dependency] private readonly SharedRadiationSystem _radiationSystem = default!;

    private static readonly ProtoId<StackPrototype> StackCredits = "Credit";

    private readonly SoundPathSpecifier _soundKaching = new("/Audio/Effects/kaching.ogg");
    private readonly SoundPathSpecifier _soundSparks = new("/Audio/Effects/sparks4.ogg");
    private readonly SoundPathSpecifier _soundPower = new("/Audio/Effects/tesla_consume.ogg");

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(SmesSystem));

        Subs.BuiEvents<PtlComponent>(PTLUiKey.Key, subs =>
        {
            subs.Event<PtlToggleMessage>(OnToggleMessage);
            subs.Event<PtlSetDelayMessage>(OnSetDelayMessage);
            subs.Event<PtlWithdrawMessage>(OnWithdrawMessage);
        });

        SubscribeLocalEvent<PtlComponent, GotEmaggedEvent>(OnEmagged);
        SubscribeLocalEvent<PtlComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PtlComponent, ChargeChangedEvent>(OnChargeChanged);
        SubscribeLocalEvent<HitscanBasicDamageComponent, HitscanTraceEvent>(OnHitscanTrace);
    }

    private void OnMapInit(Entity<PtlComponent> entity, ref MapInitEvent args)
    {
        UpdateUiState(entity);
    }

    private void OnToggleMessage(Entity<PtlComponent> entity, ref PtlToggleMessage args)
    {
        entity.Comp.Active = !entity.Comp.Active;

        if (entity.Comp.Active)
            EnsureComp<ActivePtlComponent>(entity);
        else
            RemComp<ActivePtlComponent>(entity);

        _audioSystem.PlayPvs(_soundPower, entity.Owner);

        UpdateAppearance(entity, CompOrNull<BatteryComponent>(entity));
        UpdateUiState(entity);
        Dirty(entity);
    }

    private void OnSetDelayMessage(Entity<PtlComponent> entity, ref PtlSetDelayMessage args)
    {
        entity.Comp.ShootDelay = Math.Clamp(args.Delay, entity.Comp.ShootDelayThreshold.X, entity.Comp.ShootDelayThreshold.Y);

        _audioSystem.PlayPvs(_soundSparks, entity.Owner);
        UpdateUiState(entity);
        Dirty(entity);
    }

    private void OnWithdrawMessage(Entity<PtlComponent> entity, ref PtlWithdrawMessage args)
    {
        if (entity.Comp.SpesosHeld <= 0)
            return;

        _stackSystem.SpawnAtPosition((int)entity.Comp.SpesosHeld, StackCredits, Transform(entity).Coordinates);
        entity.Comp.SpesosHeld = 0;

        _audioSystem.PlayPvs(_soundKaching, entity.Owner);
        UpdateUiState(entity);
        Dirty(entity);
    }

    private void UpdateUiState(Entity<PtlComponent> entity)
    {
        var currentCharge = 0f;
        var maxCharge = 0f;

        if (TryComp<BatteryComponent>(entity, out var battery))
        {
            currentCharge = _batterySystem.GetCharge((entity, battery));
            maxCharge = battery.MaxCharge;
        }

        _userInterfaceSystem.SetUiState(entity.Owner, PTLUiKey.Key, new PtlBoundUserInterfaceState(
            entity.Comp.Active,
            entity.Comp.SpesosHeld,
            entity.Comp.ShootDelay,
            entity.Comp.ShootDelayThreshold.X,
            entity.Comp.ShootDelayThreshold.Y,
            currentCharge,
            maxCharge));
    }

    private void OnHitscanTrace(EntityUid uid, HitscanBasicDamageComponent component, ref HitscanTraceEvent args)
    {
        if (!TryComp<PtlComponent>(args.Gun, out var ptl))
            return;

        if (!TryComp<BatteryComponent>(args.Gun, out var battery))
            return;

        var megajoule = 1e6;
        var charge = _batterySystem.GetCharge((args.Gun, battery)) / megajoule;

        component.Damage = ptl.BaseBeamDamage * (float)charge * ptl.DamageMultiplier;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var eqe = EntityQueryEnumerator<ActivePtlComponent, PtlComponent, BatteryComponent>();

        while (eqe.MoveNext(out var uid, out _, out var ptlComponent, out var batteryComponent))
        {
            if (_gameTiming.CurTime < ptlComponent.NextShotAt)
                continue;

            ptlComponent.NextShotAt = _gameTiming.CurTime + TimeSpan.FromSeconds(ptlComponent.ShootDelay);

            if (_batterySystem.GetCharge((uid, batteryComponent)) < ptlComponent.MinShootPower)
                continue;

            Shoot((uid, ptlComponent), batteryComponent);
            UpdateAppearance((uid, ptlComponent), batteryComponent);
        }
    }

    private void Shoot(Entity<PtlComponent> entity, BatteryComponent batteryComponent)
    {
        var megajoule = 1e6;
        var charge = _batterySystem.GetCharge((entity, batteryComponent)) / megajoule;

        var spesos = (int)(charge * entity.Comp.SpesosMultiplier);

        if (charge <= 0 || !double.IsFinite(spesos) || spesos < 0) return;

        if (TryComp<GunComponent>(entity, out var gun))
        {
            if (!TryComp(entity, out TransformComponent? xform))
                return;

            var localDirectionVector = Vector2.UnitY * -1f;
            if (entity.Comp.ReversedFiring)
                localDirectionVector *= -1f;

            var directionInParentSpace = xform.LocalRotation.RotateVec(localDirectionVector);
            var targetCoords = xform.Coordinates.Offset(directionInParentSpace);

            var muzzleOffset = entity.Comp.ShootOffset;
            if (entity.Comp.ReversedFiring)
                muzzleOffset *= -1f;

            var rotatedMuzzleOffset = xform.LocalRotation.RotateVec(muzzleOffset);
            var muzzleCoords = xform.Coordinates.Offset(rotatedMuzzleOffset);

            _gunSystem.AttemptShoot(entity.Owner, (entity, gun), muzzleCoords, targetCoords);
        }

        if (charge >= entity.Comp.PowerEvilThreshold)
        {
            // Square root scaling makes the intensity increase more gradually
            // e.g., 10MJ = 1.0, 40MJ = 2.0, 90MJ = 3.0, 1000MJ = 10.0
            var evil = (float)Math.Sqrt(charge / entity.Comp.PowerEvilThreshold);

            if (TryComp<RadiationSourceComponent>(entity, out var rad))
                _radiationSystem.SetIntensity((entity, rad), evil);

            // Cap the flash duration to a sane maximum of 10 seconds
            var flashTime = Math.Min(evil, 10f);
            _flashSystem.FlashArea(entity.Owner, null, evil, TimeSpan.FromSeconds(flashTime));
        }
        else
        {
            if (TryComp<RadiationSourceComponent>(entity, out var rad))
                _radiationSystem.SetIntensity((entity, rad), 0f);
        }

        entity.Comp.SpesosHeld += spesos;

        // Subtract the full charge used for firing from the battery.
        // The GunSystem also subtracts a small fireCost from the BatteryAmmoProvider, but that is negligible compared to megajoules.
        _batterySystem.UseCharge((entity, batteryComponent), (float)(charge * megajoule));

        UpdateUiState(entity);

        // Reset radiation intensity after a scaling delay (min 3s) so it pulses rather than leaks permanently.
        if (charge >= entity.Comp.PowerEvilThreshold)
        {
            // evil ranges from 1.0 (at 10MJ) to 10.0 (at 1000MJ)
            var evil = (float)Math.Sqrt(charge / entity.Comp.PowerEvilThreshold);
            var pulseTime = 3f * (float)Math.Sqrt(evil);
            entity.Comp.RadiationResetAt = _gameTiming.CurTime + TimeSpan.FromSeconds(pulseTime);

            Timer.Spawn(TimeSpan.FromSeconds(pulseTime), () =>
            {
                if (Exists(entity) && TryComp<PtlComponent>(entity.Owner, out var ptlComp) && _gameTiming.CurTime >= ptlComp.RadiationResetAt)
                {
                    if (TryComp<RadiationSourceComponent>(entity, out var radSource))
                        _radiationSystem.SetIntensity((entity, radSource), 0f);
                }
            });
        }

        Dirty(entity);
    }

    private void OnEmagged(Entity<PtlComponent> entity, ref GotEmaggedEvent args)
    {
        if (!_emagSystem.CompareFlag(args.Type, EmagType.Interaction) ||
            _emagSystem.CheckFlag(entity.Owner, EmagType.Interaction))
            return;

        if (entity.Comp.ReversedFiring)
            return;

        entity.Comp.ReversedFiring = true;
        args.Handled = true;
    }

    private void OnChargeChanged(Entity<PtlComponent> entity, ref ChargeChangedEvent args)
    {
        UpdateAppearance(entity, CompOrNull<BatteryComponent>(entity));
        UpdateUiState(entity);
    }

    private void UpdateAppearance(Entity<PtlComponent> entity, BatteryComponent? batteryComponent)
    {
        _appearance.SetData(entity.Owner, PtlVisuals.Active, entity.Comp.Active);

        if (batteryComponent is not { })
            return;

        var chargeLevel = (int)Math.Clamp(Math.Round(_batterySystem.GetCharge((entity, batteryComponent)) / batteryComponent.MaxCharge * 6), 0, 6);
        _appearance.SetData(entity.Owner, PtlVisuals.ChargeLevel, chargeLevel);
    }
}
