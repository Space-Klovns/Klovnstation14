// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._KS14.Random.Helpers;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Throwing;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Klovnmed.Dismemberment;

/// <summary>
///     All methods that search for bodyparts here assume that
///         all bodyparts will only either be contained in either a Body,
///         or another BodyPart.
/// </summary>
public sealed class DismembermentSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly ThrowingSystem _throwingSystem = default!;
    [Dependency] private readonly BodyPartSearchSystem _bodyPartSearchSystem = default!;

    private EntityQuery<BodyComponent> _bodyQuery;

    public override void Initialize()
    {
        base.Initialize();

        _bodyQuery = GetEntityQuery<BodyComponent>();
    }

    /// <returns>True if, with the given damage, something can be dismembered from the given entity.</returns>
    public bool CanDismemberByDamage(Entity<BodyComponent?> bodyEntity, DamageSpecifier damageSpecifier, [NotNullWhen(true)] out float? totalDamage)
    {
        if (!_bodyQuery.Resolve(bodyEntity, ref bodyEntity.Comp, logMissing: false))
        {
            totalDamage = null;
            return false;
        }

        var totalDamageFp2 = damageSpecifier.GetTotal();
        totalDamage = (float)damageSpecifier.GetTotal();
        return totalDamageFp2 >= bodyEntity.Comp.DismembermentThreshold;
    }

    /// <summary>
    ///     Tries to dismember a random body-part of given type from someone,
    ///         setting its coordinates to those of the victim and throwing it in a random direction.
    ///         Does no throwing logic if <paramref name="throwSpeed"/> is exactly 0f.
    ///
    ///     Supports <see cref="partType"/> having more than one bit set.
    /// </summary>
    /// <returns>Whether anything happened.</returns>
    public bool TryDismemberRandomBodyPartOfType(Entity<BodyComponent?, TransformComponent?> bodyEntity, BodyPartType partType, [NotNullWhen(true)] out Entity<BodyPartComponent>? partEntity, Vector2? direction = null, float throwSpeed = 10f, EntityUid? cause = null)
    {
        if (!_bodyQuery.Resolve(bodyEntity, ref bodyEntity.Comp1, logMissing: false) ||
            !EntityManager.TransformQuery.Resolve(bodyEntity, ref bodyEntity.Comp2, logMissing: true) ||
            !_bodyPartSearchSystem.TryGetRandomBodyPartOfType(bodyEntity, partType, out var predictedRandom, out partEntity))
        {
            partEntity = null;
            return false;
        }

        _transformSystem.SetCoordinates(partEntity.Value.Owner, bodyEntity.Comp2.Coordinates);

        if (throwSpeed != 0f)
        {
            direction ??= (predictedRandom ?? KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(
                (int)_gameTiming.CurTick.Value,
                KsSharedRandomExtensions.GetNetId(partEntity.Value.Owner, EntityManager)
            )).NextUnitVector2();

            _throwingSystem.TryThrow(partEntity.Value.Owner, direction.Value, baseThrowSpeed: throwSpeed, user: cause, recoil: false);
        }

        return true;
    }
}
