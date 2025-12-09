// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MIT

using Content.Server.EntityEffects;
using Content.Shared._KS14.Mimery;
using Content.Shared.EntityEffects;
using Content.Shared.Projectiles;
using Robust.Shared.Random;

namespace Content.Server._KS14.Mimery;

public sealed class EntityEffectOnProjectileHitSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly EntityEffectSystem _entityEffect = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EntityEffectOnProjectileHitComponent, ProjectileHitEvent>(OnHit);
    }

    private void OnHit(Entity<EntityEffectOnProjectileHitComponent> ent, ref ProjectileHitEvent args)
    {
        var effectArgs = new EntityEffectReagentArgs(args.Target, EntityManager, ent.Owner, null, 1, null, null, 1);
        foreach (var effect in ent.Comp.Effects)
        {
            var ev = new ExecuteEntityEffectEvent<EntityEffect>(effect, effectArgs);
            RaiseLocalEvent(ref ev);
        }
    }
}
