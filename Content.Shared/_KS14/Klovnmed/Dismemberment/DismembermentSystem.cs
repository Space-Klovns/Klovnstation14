using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._KS14.Random.Helpers;
using Content.Shared.Body;
using Content.Shared.Damage;
using Content.Shared.Throwing;
using Robust.Shared.Containers;
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
    [Dependency] private readonly ThrowingSystem _throwingSystem = default!;
    [Dependency] private readonly OrganSearchSystem _organSearchSystem = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;

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
        totalDamage = (float)totalDamageFp2;
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
    public bool TryDismemberRandomBodyPartOfType(Entity<BodyComponent?, TransformComponent?> bodyEntity, BodyPartType partType, [NotNullWhen(true)] out EntityUid? partUid, Vector2? direction = null, float throwSpeed = 10f, EntityUid? cause = null)
    {
        if (!_bodyQuery.Resolve(bodyEntity, ref bodyEntity.Comp1, logMissing: false) ||
            !EntityManager.TransformQuery.Resolve(bodyEntity, ref bodyEntity.Comp2, logMissing: true) ||
            !_organSearchSystem.TryGetRandomBodyPartOfType((bodyEntity, bodyEntity.Comp1), partType, out var predictedRandom, out partUid))
        {
            partUid = null;
            return false;
        }

        DismemberPart((bodyEntity.Owner, bodyEntity.Comp2), partUid.Value, direction: direction, throwSpeed: throwSpeed, cause: cause, predictedRandom: predictedRandom);
        return true;
    }

    public void DismemberPart(Entity<TransformComponent?> bodyEntity, EntityUid partUid, Vector2? direction = null, float throwSpeed = 0f, EntityUid? cause = null, System.Random? predictedRandom = null)
    {
        if (!EntityManager.TransformQuery.Resolve(bodyEntity, ref bodyEntity.Comp))
            return;

        var partTransform = Transform(partUid);
        _containerSystem.Remove(partUid, _containerSystem.GetContainer(partTransform.ParentUid, BodyHierarchySystem.ConstContainerId), force: true, destination: bodyEntity.Comp.Coordinates);

        if (throwSpeed != 0f)
        {
            direction ??= (predictedRandom ?? KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(
                (int)_gameTiming.CurTick.Value,
                KsSharedRandomExtensions.GetNetId(partUid, EntityManager)
            )).NextUnitVector2();

            _throwingSystem.TryThrow(partUid, direction.Value, baseThrowSpeed: throwSpeed, user: cause, recoil: false);
        }
    }
}
