// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Execution;

/// <summary>
/// Handles the GunExecutedEvent for cartridge-based ammunition.
/// Populates the damage specifier and marks the cartridge as spent.
/// </summary>
public sealed class CartridgeExecutionSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CartridgeAmmoComponent, GunExecutedEvent>(OnCartridgeExecuted);
        SubscribeLocalEvent<CartridgeAmmoComponent, GunFinishedExecutionEvent>(OnCartridgeFinishedExecution);
    }

    private void OnCartridgeExecuted(EntityUid uid, CartridgeAmmoComponent component, ref GunExecutedEvent args)
    {
        if (component.Spent)
        {
            args.Cancelled = true;
            return;
        }

        if (_prototypeManager.TryIndex(component.Prototype, out EntityPrototype? proto) &&
            proto.TryGetComponent<ProjectileComponent>(out var projectile, _componentFactory))
        {
            args.Damage = projectile.Damage;
        }
    }

    private void OnCartridgeFinishedExecution(Entity<CartridgeAmmoComponent> entity, ref GunFinishedExecutionEvent args)
    {
        entity.Comp.Spent = true;
        _appearanceSystem.SetData(entity.Owner, AmmoVisuals.Spent, true);
        Dirty(entity);
    }
}
