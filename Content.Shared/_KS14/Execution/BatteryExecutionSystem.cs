// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared._KS14.Execution;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Prototypes;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Weapons.Hitscan.Components;

namespace Content.Server._KS14.Execution;

/// <summary>
/// Server-side handler for GunExecutedEvent on battery-powered weapons.
/// </summary>
public sealed class BatteryExecutionSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedBatterySystem _batterySystem = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;

    public override void Initialize()
    {
        base.Initialize();
        // We subscribe on the weapon, not the battery, because the damage info is on the weapon's provider.
        SubscribeLocalEvent<BatteryAmmoProviderComponent, GunExecutedEvent>(OnBatteryAmmoProviderExecuted);
    }

    private void OnBatteryAmmoProviderExecuted(Entity<BatteryAmmoProviderComponent> entity, ref GunExecutedEvent args)
    {
        // Default to cancelled
        if (!_batterySystem.TryUseCharge(entity.Owner, entity.Comp.FireCost))
            goto onCancelled;

        if (!_prototypeManager.TryIndex(entity.Comp.Prototype, out var prototype))
            goto onCancelled;

        if (prototype.TryGetComponent<ProjectileComponent>(out var projectileComponent, _componentFactory))
            args.Damage = projectileComponent.Damage;
        else if (prototype.TryGetComponent<HitscanBasicDamageComponent>(out var hitscanDamageComponent, _componentFactory))
            args.Damage = hitscanDamageComponent.Damage;

        return;

    onCancelled:
        args.FailureReason = "execution-popup-gun-empty";
        args.Cancelled = true;

        return;
    }
}
