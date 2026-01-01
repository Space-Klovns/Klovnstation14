// SPDX-FileCopyrightText: 2024 Celene
// SPDX-FileCopyrightText: 2024 Mervill
// SPDX-FileCopyrightText: 2024 Plykiya
// SPDX-FileCopyrightText: 2024 Scribbles0
// SPDX-FileCopyrightText: 2025 Aiden
// SPDX-FileCopyrightText: 2025 Aviu00
// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 Piras314
// SPDX-FileCopyrightText: 2025 Solstice
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Execution;
using Content.Shared.Camera;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using System.Numerics;
using Content.Shared.FixedPoint;
using Content.Shared.Damage.Components;

namespace Content.Shared._KS14.Execution;

/// <summary>
///     verb for executing with guns
/// </summary>
public sealed class SharedGunExecutionSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedSuicideSystem _suicide = default!;
    //[Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly SharedExecutionSystem _execution = default!;
    [Dependency] private readonly SharedGunSystem _gunSystem = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private const float GunExecutionTime = 4.0f;

    /// <summary>
    ///     Minimum amount of damage a gun
    ///         can do to be valid for gun executions.
    /// </summary>
    public static readonly FixedPoint2 MinimumValidDamage = FixedPoint2.New(6);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunComponent, GetVerbsEvent<UtilityVerb>>(OnGetInteractionVerbsGun);
        SubscribeLocalEvent<GunComponent, ExecutionDoAfterEvent>(OnDoafterGun);

    }

    private void OnGetInteractionVerbsGun(EntityUid uid, GunComponent component, GetVerbsEvent<UtilityVerb> args)
    {
        if (args.Hands == null || args.Using == null || !args.CanAccess || !args.CanInteract)
            return;

        var attacker = args.User;
        var weapon = args.Using!.Value;
        var victim = args.Target;
        var gunexecutiontime = component.GunExecutionTime;

        if (!HasComp<GunExecutionWhitelistComponent>(weapon)
            || !CanExecuteWithGun(weapon, victim, attacker))
            return;

        UtilityVerb verb = new()
        {
            Act = () => TryStartGunExecutionDoafter(weapon, victim, attacker, gunexecutiontime),
            Impact = LogImpact.High,
            Text = Loc.GetString("execution-verb-name"),
            Message = Loc.GetString("execution-verb-message"),
        };

        args.Verbs.Add(verb);
    }

    private bool CanExecuteWithGun(EntityUid weapon, EntityUid victim, EntityUid user)
    {
        // Rifles can execute anyone.
        if (!HasComp<UnrestrictedExecutionComponent>(weapon))
        {
            if (!_execution.CanBeExecuted(victim, user))
                return false;
        }

        if (TryComp<GunComponent>(weapon, out var gun) && !_gunSystem.CanShoot(gun))
            return false;

        return true;
    }

    private void TryStartGunExecutionDoafter(EntityUid weapon, EntityUid victim, EntityUid attacker, float gunexecutiontime)
    {
        if (!CanExecuteWithGun(weapon, victim, attacker))
            return;

        if (attacker == victim)
        {
            _execution.ShowExecutionInternalPopup("suicide-popup-gun-initial-internal", attacker, victim, weapon);
            _execution.ShowExecutionExternalPopup("suicide-popup-gun-initial-external", attacker, victim, weapon);
        }
        else
        {
            _execution.ShowExecutionInternalPopup("execution-popup-gun-initial-internal", attacker, victim, weapon);
            _execution.ShowExecutionExternalPopup("execution-popup-gun-initial-external", attacker, victim, weapon);
        }

        var doAfter =
            new DoAfterArgs(EntityManager, attacker, gunexecutiontime, new ExecutionDoAfterEvent(), weapon, target: victim, used: weapon)
            {
                BreakOnMove = true,
                BreakOnDamage = true,
                NeedHand = true,
            };

        _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnDoafterGun(EntityUid uid, GunComponent component, DoAfterEvent args)
    {
        if (_net.IsClient &&
            !_timing.IsFirstTimePredicted)
            return;

        if (args.Handled
            || args.Cancelled
            || args.Used == null
            || args.Target == null)
            return;

        var attacker = args.User;
        var victim = args.Target.Value;
        var weapon = args.Used.Value;

        // Get the direction for the recoil
        var direction = Vector2.Zero;
        var attackerXform = Transform(attacker);
        var victimXform = Transform(victim);

        // Use SharedTransformSystem instead of obsolete WorldPosition
        var diff = _transform.GetWorldPosition(victimXform) - _transform.GetWorldPosition(attackerXform);

        if (diff != Vector2.Zero)
            direction = -diff.Normalized(); // recoil opposite of shot

        if (!CanExecuteWithGun(weapon, victim, attacker)
            || !TryComp<DamageableComponent>(victim, out var damageableComponent))
            return;

        // Take ammo
        // Run on both Client and Server to ensure prediction works
        var fromCoordinates = attackerXform.Coordinates;
        var ev = new TakeAmmoEvent(1, new(), fromCoordinates, attacker);
        RaiseLocalEvent(weapon, ev);

        // Signal to the gun system that its appearance needs updating
        var updateEv = new GunNeedsAppearanceUpdateEvent();
        RaiseLocalEvent(weapon, ref updateEv);

        // Check for empty
        if (ev.Ammo.Count <= 0)
        {
            _audio.PlayPredicted(component.SoundEmpty, uid, attacker);
            _execution.ShowExecutionInternalPopup("execution-popup-gun-empty", attacker, victim, weapon);
            _execution.ShowExecutionExternalPopup("execution-popup-gun-empty", attacker, victim, weapon);
            return;
        }

        var ammoUid = ev.Ammo[0].Entity;

        // Raise an event on the ammo to get its damage and handle its consumption.
        var gunExecutedEvent = new GunExecutedEvent(attacker, victim);

        // If the TakeAmmoEvent returns an entity, we raise the event on that entity.
        if (ammoUid.HasValue)
            RaiseLocalEvent(ammoUid.Value, ref gunExecutedEvent);
        // If not (e.g. for battery weapons), we raise it on the gun itself.
        else
            RaiseLocalEvent(weapon, ref gunExecutedEvent);

        var damage = gunExecutedEvent.Damage;
        if (damage == null || damage.GetTotal() < MinimumValidDamage)
        {
            _execution.ShowExecutionInternalPopup("execution-popup-gun-weak-ammo", attacker, victim, weapon, predict: true);
            _execution.ShowExecutionExternalPopup("execution-popup-gun-weak-ammo", attacker, victim, weapon);
            return;
        }

        // Check if the execution was cancelled by one of the ammo systems for whatever reason
        if (gunExecutedEvent.Cancelled)
        {
            _audio.PlayPredicted(component.SoundEmpty, uid, attacker);
            var reason = gunExecutedEvent.FailureReason ?? "execution-popup-gun-empty";
            _execution.ShowExecutionInternalPopup(reason, attacker, victim, weapon, predict: true);
            _execution.ShowExecutionExternalPopup(reason, attacker, victim, weapon);
            return;
        }

        var finishedEv = new GunFinishedExecutionEvent();
        RaiseLocalEvent(weapon, ref finishedEv);

        // Effects and damage
        //var prev = _combat.IsInCombatMode(attacker);
        //_combat.SetInCombatMode(attacker, true);

        // Play sound
        // This is now outside the Server check so the client hears it immediately
        _audio.PlayPredicted(component.SoundGunshot, uid, attacker);

        // Damage and popups
        // Damage must be authoritative.
        var messagePrefix = attacker == victim ? "suicide" : "execution";

        _execution.ShowExecutionInternalPopup($"{messagePrefix}-popup-gun-complete-internal", attacker, victim, weapon, predict: true);
        _execution.ShowExecutionExternalPopup($"{messagePrefix}-popup-gun-complete-external", attacker, victim, weapon);
        _suicide.ApplyLethalDamage((victim, damageableComponent), damage);

        // Client-side prediction for recoil
        if (_net.IsClient && // because KickCamera on server networks it to be called on client
            direction != Vector2.Zero)
            _recoil.KickCamera(attacker, direction);

        //_combat.SetInCombatMode(attacker, prev);
        args.Handled = true;
    }
}
