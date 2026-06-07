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
using Content.Shared.Power.Components;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Robust.Server.Audio;
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
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedBatterySystem _battery = default!;
    [Dependency] private readonly SharedRadiationSystem _radiation = default!;

    private static readonly ProtoId<StackPrototype> _stackCredits = "Credit";
    private static readonly ProtoId<TagPrototype> _tagScrewdriver = "Screwdriver";
    private static readonly ProtoId<TagPrototype> _tagMultitool = "Multitool";

    private readonly SoundPathSpecifier _soundKaching = new("/Audio/Effects/kaching.ogg");
    private readonly SoundPathSpecifier _soundSparks = new("/Audio/Effects/sparks4.ogg");
    private readonly SoundPathSpecifier _soundPower = new("/Audio/Effects/tesla_consume.ogg");

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(SmesSystem));
        SubscribeLocalEvent<PTLComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<PTLComponent, AfterInteractUsingEvent>(OnAfterInteractUsing);
        SubscribeLocalEvent<PTLComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<PTLComponent, GotEmaggedEvent>(OnEmagged);
        SubscribeLocalEvent<PTLComponent, ChargeChangedEvent>(OnChargeChanged);
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

    private void OnInteractHand(Entity<PTLComponent> ent, ref InteractHandEvent args)
    {
        ent.Comp.Active = !ent.Comp.Active;
        
        if (ent.Comp.Active)
            EnsureComp<PTLActiveComponent>(ent);
        else
            RemComp<PTLActiveComponent>(ent);

        var enloc = ent.Comp.Active ? Loc.GetString("ptl-enabled") : Loc.GetString("ptl-disabled");
        var enabled = Loc.GetString("ptl-interact-enabled", ("enabled", enloc));
        _popup.PopupEntity(enabled, ent, Content.Shared.Popups.PopupType.SmallCaution);
        _aud.PlayPvs(_soundPower, args.User);

        UpdateAppearance(ent, ent.Comp, CompOrNull<BatteryComponent>(ent));
        Dirty(ent);
    }

    private void OnAfterInteractUsing(Entity<PTLComponent> ent, ref AfterInteractUsingEvent args)
    {
        var held = args.Used;

        if (_tag.HasTag(held, _tagScrewdriver))
        {
            var delay = ent.Comp.ShootDelay + 1;
            if (delay > ent.Comp.ShootDelayThreshold.Y)
                delay = ent.Comp.ShootDelayThreshold.X;
            ent.Comp.ShootDelay = delay;
            _popup.PopupEntity(Loc.GetString("ptl-interact-screwdriver", ("delay", ent.Comp.ShootDelay)), ent);
            _aud.PlayPvs(_soundSparks, args.User);
        }

        if (_tag.HasTag(held, _tagMultitool))
        {
            _stack.SpawnAtPosition((int) ent.Comp.SpesosHeld, _stackCredits, Transform(args.User).Coordinates);
            ent.Comp.SpesosHeld = 0;
            _popup.PopupEntity(Loc.GetString("ptl-interact-spesos"), ent);
            _aud.PlayPvs(_soundKaching, args.User);
        }

        Dirty(ent);
    }

    private void OnExamine(Entity<PTLComponent> ent, ref ExaminedEvent args)
    {
        var sb = new StringBuilder();
        var enloc = ent.Comp.Active ? Loc.GetString("ptl-enabled") : Loc.GetString("ptl-disabled");
        sb.AppendLine(Loc.GetString("ptl-examine-enabled", ("enabled", enloc)));
        sb.AppendLine(Loc.GetString("ptl-examine-spesos", ("spesos", ent.Comp.SpesosHeld)));
        sb.AppendLine(Loc.GetString("ptl-examine-screwdriver"));
        args.PushMarkup(sb.ToString());
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
