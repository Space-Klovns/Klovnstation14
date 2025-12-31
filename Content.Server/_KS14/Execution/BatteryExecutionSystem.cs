// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared._KS14.Execution;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged;
using Content.Server.Power.EntitySystems;
using Content.Shared.Projectiles;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Execution;

// TODO LCDC: predictedbattery oneday

/// <summary>
/// Server-side handler for GunExecutedEvent on battery-powered weapons.
/// </summary>
public sealed class BatteryExecutionSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly BatterySystem _batterySystem = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;

    public override void Initialize()
    {
        base.Initialize();
        // We subscribe on the weapon, not the battery, because the damage info is on the weapon's provider.
        SubscribeLocalEvent<HitscanBatteryAmmoProviderComponent, GunExecutedEvent>(OnHitscanBatteryExecuted);
        SubscribeLocalEvent<ProjectileBatteryAmmoProviderComponent, GunExecutedEvent>(OnProjectileBatteryExecuted);
    }

    private void OnHitscanBatteryExecuted(EntityUid uid, HitscanBatteryAmmoProviderComponent component, ref GunExecutedEvent args)
    {
        if (!_batterySystem.TryUseCharge(uid, component.FireCost))
            return;

        if (!_prototypeManager.TryIndex(component.Prototype, out HitscanPrototype? proto))
            return;

        args.Damage = proto.Damage;
    }

    private void OnProjectileBatteryExecuted(EntityUid uid, ProjectileBatteryAmmoProviderComponent component, ref GunExecutedEvent args)
    {
        if (!_batterySystem.TryUseCharge(uid, component.FireCost))
            return;

        if (!_prototypeManager.TryIndex(component.Prototype, out var proto) ||
            !proto.TryGetComponent<ProjectileComponent>(out var projectile, _componentFactory))
            return;

        args.Damage = projectile.Damage;
    }
}
