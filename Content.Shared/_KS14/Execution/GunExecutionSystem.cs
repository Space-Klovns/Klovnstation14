// SPDX-FileCopyrightText: 2024 Celene
// SPDX-FileCopyrightText: 2024 Mervill
// SPDX-FileCopyrightText: 2024 Plykiya
// SPDX-FileCopyrightText: 2024 Scribbles0
// SPDX-FileCopyrightText: 2025 Aiden
// SPDX-FileCopyrightText: 2025 Aviu00
// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 Piras314
// SPDX-FileCopyrightText: 2025 Solstice
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.ActionBlocker;
using Content.Shared.Chat;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Entry;
using Content.Shared.Interaction.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Projectiles;
using Content.Shared.Execution;
using Content.Shared.Camera;
using Robust.Shared.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Containers;
using Content.Shared.Containers.ItemSlots;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
using System.Numerics;

namespace Content.Shared._KS14.Execution;

/// <summary>
///     verb for executing with guns
/// </summary>
public sealed class SharedGunExecutionSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedSuicideSystem _suicide = default!;
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly SharedExecutionSystem _execution = default!;
    [Dependency] private readonly SharedGunSystem _gunSystem = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    private const float GunExecutionTime = 4.0f;

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
        if (!_execution.CanBeExecuted(victim, user)
            || TryComp<GunComponent>(weapon, out var gun)
            && !_gunSystem.CanShoot(gun))
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

    private string GetDamage(DamageSpecifier damage, string? mainDamageType)
    {
        // Default fallback if nothing valid found
        mainDamageType ??= "Blunt";

        if (damage == null || damage.DamageDict.Count == 0)
            return mainDamageType;

        var filtered = damage.DamageDict
            .Where(kv => !string.Equals(kv.Key, "Structural", StringComparison.OrdinalIgnoreCase));

        if (filtered.Any())
        {
            mainDamageType = filtered.Aggregate((a, b) => a.Value > b.Value ? a : b).Key;
        }

        return mainDamageType ?? "Blunt";
    }

    private void OnDoafterGun(EntityUid uid, GunComponent component, DoAfterEvent args)
    {
        if (args.Handled
            || args.Cancelled
            || args.Used == null
            || args.Target == null
            || !TryComp<GunComponent>(uid, out var guncomp))
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
        var fromCoordinates = Transform(attacker).Coordinates;
        var ev = new TakeAmmoEvent(1, new List<(EntityUid? Entity, IShootable Shootable)>(), fromCoordinates, attacker);
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

        var damage = new DamageSpecifier();
        string? mainDamageType = null;
        var ammoUid = ev.Ammo[0].Entity;

        // Process ammo
        switch (ev.Ammo[0].Shootable)
        {
            // This case handles guns that eject a whole cartridge, like pistols.
            case CartridgeAmmoComponent cartridge:
                if (cartridge.Spent)
                {
                    _audio.PlayPredicted(component.SoundEmpty, uid, attacker);
                    return;
                }

                if (_prototypeManager.TryIndex(cartridge.Prototype, out EntityPrototype? proto) &&
                    proto.TryGetComponent<ProjectileComponent>(out var projectile, _componentFactory))
                {
                    damage = projectile.Damage;
                    mainDamageType = GetDamage(damage, mainDamageType);
                }

                // Mark the cartridge as spent. The gun's ammo provider is responsible for ejecting it.
                cartridge.Spent = true;
                _appearanceSystem.SetData(ammoUid!.Value, AmmoVisuals.Spent, true);
                Dirty(ammoUid!.Value, cartridge);
                break;

            // This case handles revolvers (which provide a bullet) and energy weapons.
            case AmmoComponent:
                if (TryComp<ProjectileComponent>(ammoUid, out var proj))
                {
                    damage = proj.Damage;
                    mainDamageType = GetDamage(damage, mainDamageType);
                }

                // The bullet from a revolver is temporary and should be deleted.
                // Energy weapons don't have a physical entity.
                if (ammoUid.HasValue)
                    Del(ammoUid.Value);
                break;
        }

        // Final damage check for all gun types
        if (damage.GetTotal() < 5 && !HasComp<HitscanBatteryAmmoProviderComponent>(weapon))
        {
            _audio.PlayPredicted(component.SoundEmpty, uid, attacker);
            _execution.ShowExecutionInternalPopup("execution-popup-gun-empty", attacker, victim, weapon);
            _execution.ShowExecutionExternalPopup("execution-popup-gun-empty", attacker, victim, weapon);
            return;
        }

        if (HasComp<HitscanBatteryAmmoProviderComponent>(weapon))
            mainDamageType = "Heat";

        // Effects and damage
        var prev = _combat.IsInCombatMode(attacker);
        _combat.SetInCombatMode(attacker, true);

        // Play sound
        // This is now outside the Server check so the client hears it immediately
        _audio.PlayPredicted(component.SoundGunshot, uid, attacker);

        // Damage and popus
        // Damage must be authoritative.
        if (_net.IsServer)
        {
            if (attacker == victim)
            {
                _execution.ShowExecutionInternalPopup("suicide-popup-gun-complete-internal", attacker, victim, weapon);
                _execution.ShowExecutionExternalPopup("suicide-popup-gun-complete-external", attacker, victim, weapon);
                _suicide.ApplyLethalDamage((victim, damageableComponent), mainDamageType);
            }
            else
            {
                _execution.ShowExecutionInternalPopup("execution-popup-gun-complete-internal", attacker, victim, weapon);
                _execution.ShowExecutionExternalPopup("execution-popup-gun-complete-external", attacker, victim, weapon);
                _suicide.ApplyLethalDamage((victim, damageableComponent), mainDamageType);
            }
        }
        else
        {
            // Client-side prediction for recoil
            if (direction != Vector2.Zero && _timing.IsFirstTimePredicted)
                _recoil.KickCamera(attacker, direction);
        }

        _combat.SetInCombatMode(attacker, prev);
        args.Handled = true;
    }
}
