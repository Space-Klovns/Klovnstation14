// SPDX-FileCopyrightText: 2026 FrauZj
// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 nabegator220
//
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Stunnable;
using Robust.Shared.Physics.Systems;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Shared._KS14.ComplexShove;

/// <summary>
///     Handles complex shoving.
/// </summary>
public sealed class ComplexShoveSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly SharedStaminaSystem _staminaSystem = default!;
    [Dependency] private readonly RayCastSystem _rayCastSystem = default!;
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsUidDown(EntityUid uid)
        => HasComp<KnockedDownComponent>(uid) || _mobStateSystem.IsIncapacitated(uid);

    public bool TryGetDeltaUnitSafe(Entity<TransformComponent?> shoverEntity, Entity<TransformComponent?> shovedEntity, [NotNullWhen(true)] out Vector2? deltaUnit, [NotNullWhen(true)] out Vector2? shoverWorldPosition)
    {
        if (!EntityManager.TransformQuery.Resolve(shoverEntity, ref shoverEntity.Comp) ||
            !EntityManager.TransformQuery.Resolve(shovedEntity, ref shovedEntity.Comp))
        {
            deltaUnit = null;
            shoverWorldPosition = null;
            return false;
        }

        shoverWorldPosition = _transformSystem.GetWorldPosition(shoverEntity.Comp);

        // trolling is not permitted
        deltaUnit = _transformSystem.GetWorldPosition(shovedEntity.Comp) - shoverWorldPosition;
        if (deltaUnit.Value.LengthSquared() <= float.Epsilon)
            return false;

        deltaUnit = deltaUnit.Value.Normalized();
        return true;
    }

    /// <summary>
    ///     Assumes that shover and shoved are both on the same map.
    /// </summary>
    /// <returns>Whether wallshove did anything.</returns>
    public bool TryWallshove(Entity<TransformComponent?, ComplexShoveComponent?> shoverEntity, Entity<StaminaComponent> shovedEntity, Vector2 shoverWorldPosition, Vector2 deltaUnit)
    {
        if (!EntityManager.TransformQuery.Resolve(shoverEntity, ref shoverEntity.Comp1) ||
            !Resolve(shoverEntity, ref shoverEntity.Comp2, logMissing: false))
            return false;

        var rayResult = _rayCastSystem.CastRayClosest(
            _transformSystem.GetMapId((shoverEntity, shoverEntity.Comp1)),
            shoverWorldPosition,
            deltaUnit * shoverEntity.Comp2.WallshoveRange,
            new QueryFilter() { LayerBits = 0L, Flags = QueryFlags.Static, MaskBits = shoverEntity.Comp2.WallshoveCollisionMask }
        );
        if (!rayResult.Hit)
            return false;

        _staminaSystem.TakeStaminaDamage(
            shovedEntity.Owner,
            shovedEntity.Comp.CritThreshold * shoverEntity.Comp2.WallshoveStaminaDamageFraction,
            component: shovedEntity.Comp,
            source: shoverEntity,
            ignoreResist: true
        );

        var pushPower = IsUidDown(shovedEntity) ? shoverEntity.Comp2.DownedPushPower : shoverEntity.Comp2.StandingPushPower;
        _physicsSystem.ApplyLinearImpulse(shovedEntity.Owner, deltaUnit * pushPower);

        return true;
    }

    /// <returns>Whether basic shove did anything.</returns>
    public bool TryBasicShove(Entity<ComplexShoveComponent?> shoverEntity, Entity<StaminaComponent> shovedEntity, Vector2 deltaUnit)
    {
        if (!Resolve(shoverEntity, ref shoverEntity.Comp, logMissing: false))
            return false;

        _staminaSystem.TakeStaminaDamage(
            shovedEntity.Owner,
            shovedEntity.Comp.CritThreshold * shoverEntity.Comp.BasicShoveStaminaDamageFraction,
            component: shovedEntity.Comp,
            source: shoverEntity,
            ignoreResist: true
        );

        var pushPower = IsUidDown(shovedEntity) ? shoverEntity.Comp.DownedPushPower : shoverEntity.Comp.StandingPushPower;
        _physicsSystem.ApplyLinearImpulse(shovedEntity.Owner, deltaUnit * pushPower);

        return true;
    }

    /// <summary>
    ///     Tries both wallshove and normal shove. Assumes that
    ///         shover and shoved are both on the same map.
    /// </summary>
    /// <returns>Whether anything happened.</returns>
    public bool TryComplexShove(Entity<ComplexShoveComponent?> shoverEntity, Entity<StaminaComponent> shovedEntity)
    {
        if (!Resolve(shoverEntity, ref shoverEntity.Comp, logMissing: false))
            return false;


        // nothing happened
        return false;
    }
}
