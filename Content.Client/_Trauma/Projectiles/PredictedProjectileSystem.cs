// SPDX-FileCopyrightText: 2026 deltanedas
// SPDX-FileCopyrightText: 2026 github_actions[bot]
// SPDX-FileCopyrightText: 2026 nabegator220
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Shared.Projectiles;
using Content.Shared._Trauma.Projectiles;
using Robust.Client.GameObjects;
using Robust.Client.Physics;

namespace Content.Client._Trauma.Projectiles;

/// <summary>
/// Hides the server-spawned projectile when firing a predicted gun.
/// </summary>
public sealed class PredictedProjectileSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ProjectileComponent, UpdateIsPredictedEvent>(OnUpdateIsPredicted);
        SubscribeNetworkEvent<ShotPredictedProjectileEvent>(OnShotPredictedProjectile);
    }

    private void OnUpdateIsPredicted(Entity<ProjectileComponent> ent, ref UpdateIsPredictedEvent args)
    {
        args.IsPredicted = true;
    }

    private void OnShotPredictedProjectile(ShotPredictedProjectileEvent args)
    {
        var uid = GetEntity(args.Projectile);
        if (uid.IsValid()) // client may not have received the projectile state yet
            _sprite.SetVisible(uid, false);
    }
}
