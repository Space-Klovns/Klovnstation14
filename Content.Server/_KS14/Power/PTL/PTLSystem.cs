using Content.Shared._KS14.Power.PTL;
using Content.Shared.Flash;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Power.SMES;
using Content.Server.Stack;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Emag.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Radiation.Components;
using Content.Shared.Radiation.Systems;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Damage;
using Content.Shared.Power.Components;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using System.Numerics;
using System.Text;
using System;

namespace Content.Server._KS14.Power.PTL;

public sealed partial class PTLSystem : EntitySystem
{
    [Dependency] private readonly GunSystem _gun = default!;
    [Dependency] private readonly IGameTiming _time = default!;
    [Dependency] private readonly SharedFlashSystem _flash = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly AudioSystem _aud = default!;
    [Dependency] private readonly EmagSystem _emag = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedBatterySystem _battery = default!;
    [Dependency] private readonly SharedRadiationSystem _radiation = default!;

    private static readonly ProtoId<StackPrototype> _stackCredits = "Credit";

    private readonly SoundPathSpecifier _soundKaching = new("/Audio/Effects/kaching.ogg");
    private readonly SoundPathSpecifier _soundSparks = new("/Audio/Effects/sparks4.ogg");
    private readonly SoundPathSpecifier _soundPower = new("/Audio/Effects/tesla_consume.ogg");

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(SmesSystem));

        Subs.BuiEvents<PTLComponent>(PTLUiKey.Key, subs =>
        {
            subs.Event<PTLToggleMessage>(OnToggleMessage);
            subs.Event<PTLSetDelayMessage>(OnSetDelayMessage);
            subs.Event<PTLWithdrawMessage>(OnWithdrawMessage);
        });

        SubscribeLocalEvent<PTLComponent, GotEmaggedEvent>(OnEmagged);
        SubscribeLocalEvent<PTLComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PTLComponent, ChargeChangedEvent>(OnChargeChanged);
        SubscribeLocalEvent<HitscanBasicDamageComponent, HitscanTraceEvent>(OnHitscanTrace);
    }

    private void OnMapInit(Entity<PTLComponent> ent, ref MapInitEvent args)
    {
        UpdateUiState(ent, ent.Comp);
    }

    private void OnToggleMessage(Entity<PTLComponent> ent, ref PTLToggleMessage args)
    {
        ent.Comp.Active = !ent.Comp.Active;

        if (ent.Comp.Active)
            EnsureComp<PTLActiveComponent>(ent);
        else
            RemComp<PTLActiveComponent>(ent);

        _aud.PlayPvs(_soundPower, ent);

        UpdateAppearance(ent, ent.Comp, CompOrNull<BatteryComponent>(ent));
        UpdateUiState(ent, ent.Comp);
        Dirty(ent);
    }

    private void OnSetDelayMessage(Entity<PTLComponent> ent, ref PTLSetDelayMessage args)
    {
        ent.Comp.ShootDelay = Math.Clamp(args.Delay, ent.Comp.ShootDelayThreshold.X, ent.Comp.ShootDelayThreshold.Y);
        
        _aud.PlayPvs(_soundSparks, ent);
        UpdateUiState(ent, ent.Comp);
        Dirty(ent);
    }

    private void OnWithdrawMessage(Entity<PTLComponent> ent, ref PTLWithdrawMessage args)
    {
        if (ent.Comp.SpesosHeld <= 0)
            return;

        _stack.SpawnAtPosition((int) ent.Comp.SpesosHeld, _stackCredits, Transform(ent).Coordinates);
        ent.Comp.SpesosHeld = 0;

        _aud.PlayPvs(_soundKaching, ent);
        UpdateUiState(ent, ent.Comp);
        Dirty(ent);
    }

    private void UpdateUiState(EntityUid uid, PTLComponent ptl)
    {
        var currentCharge = 0f;
        var maxCharge = 0f;

        if (TryComp<BatteryComponent>(uid, out var battery))
        {
            currentCharge = _battery.GetCharge((uid, battery));
            maxCharge = battery.MaxCharge;
        }

        _ui.SetUiState(uid, PTLUiKey.Key, new PTLBoundUserInterfaceState(
            ptl.Active, 
            ptl.SpesosHeld, 
            ptl.ShootDelay, 
            ptl.ShootDelayThreshold.X, 
            ptl.ShootDelayThreshold.Y,
            currentCharge,
            maxCharge));
    }

    private void OnHitscanTrace(EntityUid uid, HitscanBasicDamageComponent component, ref HitscanTraceEvent args)
    {
        if (!TryComp<PTLComponent>(args.Gun, out var ptl))
            return;

        if (!TryComp<BatteryComponent>(args.Gun, out var battery))
            return;

        var megajoule = 1e6;
        var charge = _battery.GetCharge((args.Gun, battery)) / megajoule;

        component.Damage = ptl.BaseBeamDamage * (float) charge * ptl.DamageMultiplier;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var eqe = EntityQueryEnumerator<PTLActiveComponent, PTLComponent, BatteryComponent>();

        while (eqe.MoveNext(out var uid, out var active, out var ptl, out var battery))
        {
            if (_time.CurTime < ptl.NextShotAt)
                continue;

            ptl.NextShotAt = _time.CurTime + TimeSpan.FromSeconds(ptl.ShootDelay);

            if (_battery.GetCharge((uid, battery)) < ptl.MinShootPower)
                continue;

            Shoot(uid, ptl, battery);
            UpdateAppearance(uid, ptl, battery);
        }
    }

    private void Shoot(EntityUid uid, PTLComponent ptl, BatteryComponent battery)
    {
        var megajoule = 1e6;
        var charge = _battery.GetCharge((uid, battery)) / megajoule;

        var spesos = (int) (charge * ptl.SpesosMultiplier);

        if (charge <= 0 || !double.IsFinite(spesos) || spesos < 0) return;

        if (TryComp<GunComponent>(uid, out var gun))
        {
            if (!TryComp(uid, out TransformComponent? xform))
                return;

            var localDirectionVector = Vector2.UnitY * -1f;
            if (ptl.ReversedFiring)
                localDirectionVector *= -1f;

            var directionInParentSpace = xform.LocalRotation.RotateVec(localDirectionVector);
            var targetCoords = xform.Coordinates.Offset(directionInParentSpace);

            var muzzleOffset = ptl.ShootOffset;
            if (ptl.ReversedFiring)
                muzzleOffset *= -1f;

            var rotatedMuzzleOffset = xform.LocalRotation.RotateVec(muzzleOffset);
            var muzzleCoords = xform.Coordinates.Offset(rotatedMuzzleOffset);

            _gun.AttemptShoot(uid, (uid, gun), muzzleCoords, targetCoords);
        }

        if (charge >= ptl.PowerEvilThreshold)
        {
            // Square root scaling makes the intensity increase more gradually
            // e.g., 10MJ = 1.0, 40MJ = 2.0, 90MJ = 3.0, 1000MJ = 10.0
            var evil = (float) Math.Sqrt(charge / ptl.PowerEvilThreshold);

            if (TryComp<RadiationSourceComponent>(uid, out var rad))
                _radiation.SetIntensity((uid, rad), evil);

            // Cap the flash duration to a sane maximum of 10 seconds
            var flashTime = Math.Min(evil, 10f);
            _flash.FlashArea(uid, null, evil, TimeSpan.FromSeconds(flashTime));
        }
        else
        {
            if (TryComp<RadiationSourceComponent>(uid, out var rad))
                _radiation.SetIntensity((uid, rad), 0f);
        }

        ptl.SpesosHeld += spesos;

        // Subtract the full charge used for firing from the battery.
        // The GunSystem also subtracts a small fireCost from the BatteryAmmoProvider, but that is negligible compared to megajoules.
        _battery.UseCharge((uid, battery), (float) (charge * megajoule));

        UpdateUiState(uid, ptl);

        // Reset radiation intensity after a scaling delay (min 3s) so it pulses rather than leaks permanently.
        if (charge >= ptl.PowerEvilThreshold)
        {
            // evil ranges from 1.0 (at 10MJ) to 10.0 (at 1000MJ)
            var evil = (float) Math.Sqrt(charge / ptl.PowerEvilThreshold);
            var pulseTime = 3f * (float) Math.Sqrt(evil);
            ptl.RadiationResetAt = _time.CurTime + TimeSpan.FromSeconds(pulseTime);

            Robust.Shared.Timing.Timer.Spawn(TimeSpan.FromSeconds(pulseTime), () =>
            {
                if (Exists(uid) && TryComp<PTLComponent>(uid, out var ptlComp) && _time.CurTime >= ptlComp.RadiationResetAt)
                {
                    if (TryComp<RadiationSourceComponent>(uid, out var radSource))
                        _radiation.SetIntensity((uid, radSource), 0f);
                }
            });
        }

        Dirty(uid, ptl);
    }

    private void OnEmagged(EntityUid uid, PTLComponent component, ref GotEmaggedEvent args)
    {
        if (!_emag.CompareFlag(args.Type, EmagType.Interaction))
            return;

        if (_emag.CheckFlag(uid, EmagType.Interaction))
            return;

        if (component.ReversedFiring)
            return;

        component.ReversedFiring = true;
        args.Handled = true;
    }

    private void OnChargeChanged(Entity<PTLComponent> ent, ref ChargeChangedEvent args)
    {
        UpdateAppearance(ent, ent.Comp, CompOrNull<BatteryComponent>(ent));
        UpdateUiState(ent, ent.Comp);
    }

    private void UpdateAppearance(EntityUid uid, PTLComponent ptl, BatteryComponent? battery)
    {
        if (battery != null)
        {
            int chargeLevel = (int)Math.Clamp(Math.Round(_battery.GetCharge((uid, battery)) / battery.MaxCharge * 6), 0, 6);
            _appearance.SetData(uid, PTLVisuals.ChargeLevel, chargeLevel);
        }
        _appearance.SetData(uid, PTLVisuals.Active, ptl.Active);
    }
}
