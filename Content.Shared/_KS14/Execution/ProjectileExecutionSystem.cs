// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Projectiles;

namespace Content.Shared._KS14.Execution;

/// <summary>
/// Handles the GunExecutedEvent for projectile-based ammunition,
/// such as bullets provided by a revolver (since they provide a projectile and not cartridge, so that the cartridge stays in the revolver.)
/// </summary>
public sealed class ProjectileExecutionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ProjectileComponent, GunExecutedEvent>(OnProjectileExecuted);
    }

    private void OnProjectileExecuted(Entity<ProjectileComponent> entity, ref GunExecutedEvent args)
    {
        args.Damage = entity.Comp.Damage;

        // Necessary to delete immediately instead of queuing
        Del(entity.Owner);
    }
}
