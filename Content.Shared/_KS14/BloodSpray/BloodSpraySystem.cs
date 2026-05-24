using System.Numerics;
using Content.Shared._KS14.OverlayStains;
using Content.Shared._KS14.Random.Helpers;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Decals;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.BloodSpray;

public sealed class BloodSpraySystem : EntitySystem
{

    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private readonly SharedDecalSystem _decalSystem = default!; // KS14 Addition
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!; // KS14 Addition
    [Dependency] private readonly StainSystem _stainSystem = default!; // KS14 Addition
    [Dependency] private readonly EntityLookupSystem _lookupSystem = default!; // KS14 Addition
    [Dependency] private readonly RayCastSystem _rayCastSystem = default!; // KS14 Addition

    private static readonly QueryFilter StaticQueryFilter = new() { LayerBits = 1L, Flags = QueryFlags.Static, MaskBits = (long)CollisionGroup.Impassable };
    private static readonly Vector2 DecalOffset = Vector2.One / 2; // KS14 Addition; this is related to texture size of the blood splatter.

    private EntityUid RecursivelyGetGridOrMapUid(TransformComponent transformComponent)
    {
        if (transformComponent.GridUid is { })
            return transformComponent.GridUid.Value;
        else if (transformComponent.MapUid == transformComponent.ParentUid)
            return transformComponent.ParentUid;

        return RecursivelyGetGridOrMapUid(Transform(transformComponent.ParentUid));
    }

    public void HandleBleedEffects(Entity<BloodstreamComponent?> entity, DamageSpecifier bloodlossSpecifier, EntityUid originUid)
        => HandleBleedEffects(entity, (float)bloodlossSpecifier.GetTotal(), originUid);

    public void HandleBleedEffects(Entity<BloodstreamComponent?> entity, float bloodloss, EntityUid originUid)
    {
        if (!Resolve(entity, ref entity.Comp) ||
            TerminatingOrDeleted(originUid))
            return;

        var targetTransform = Transform(entity);
        var originTransform = Transform(originUid);

        var worldDeltaUnit = _transformSystem.GetWorldPosition(targetTransform) - _transformSystem.GetWorldPosition(originTransform);

        Vector2Helpers.Normalize(ref worldDeltaUnit);
        HandleBleedEffects(entity!, bloodloss, targetTransform, RecursivelyGetGridOrMapUid(originTransform), worldDeltaUnit);
    }

    // TODO: Clean up code
    // Coder's Ultimatum
    // TODO LCDC: FUCKING FIX THIS HOLY SHIT! IITS SO EASY
    // TODO LCDC: READ ABOVE
    public void HandleBleedEffects(Entity<BloodstreamComponent> entity, float bloodloss, TransformComponent targetTransform, EntityUid parentUid, Vector2 worldDeltaUnit)
    {
        // it shouldnt be 0 ever anyway
        if (bloodloss <= 5f)
            return;

        if (!_solutionContainerSystem.ResolveSolution(entity.Owner, entity.Comp.BloodSolutionName, ref entity.Comp.BloodSolution, out var bloodSolution))
            return;

        // TODO: fix occasional mispredict here
        var predictedRandom = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed((int)_gameTiming.CurTick.Value, KsSharedRandomExtensions.GetNetId(entity.Owner, EntityManager));
        var parentInvWorldMatrix = _transformSystem.GetInvWorldMatrix(parentUid);

        var bloodColor = bloodSolution.GetColor(_prototypeManager);
        bloodColor = bloodColor.WithAlpha(bloodColor.A * predictedRandom.NextFloat(0.12f, 0.2f)); // random alpha

        const float maxPower = 1.75f;
        var power = MathF.Max(maxPower * (1f - MathF.Exp(-bloodloss / 7.8f)), 0f);

        const float iterationDelta = 0.25f;
        var iteratedPower = (int)(power / iterationDelta);
        var cachedVariations = new Vector2[iteratedPower + 1];
        var totalVariation = Vector2.Zero;

        iteratedPower -= 1;
        for (; iteratedPower >= 0; iteratedPower--)
        {
            var variation = predictedRandom.NextPolarVector2(-0.25f, 0.25f);
            cachedVariations[iteratedPower] = variation;
            totalVariation += variation;
        }

        var localDeltaUnit = worldDeltaUnit;
        if (targetTransform.ParentUid != targetTransform.MapUid)
            // `Transform` instead of `TransformNormal` would apply translation which would be HORRIBLE
            localDeltaUnit = Vector2.TransformNormal(worldDeltaUnit, parentInvWorldMatrix);

        RayResult rayResult = new();
        _rayCastSystem.CastRayClosest(
            parentUid,
            ref rayResult,
            targetTransform.LocalPosition,
            localDeltaUnit * power + totalVariation,
            StaticQueryFilter
        );

        EntityCoordinates effectCoordinates;
        if (rayResult.Hit)
        {
            var hitData = rayResult.Results[0];

            // docs for RayHit lie because RayHit.Point isnt the *caller* changing it to local terms, but instead the code constructing it; it is also local to the hit entity's parent(? TODO: confirm that assumption)
            // tldr `hitData.Point` is local to `hitData.Entity`'s ParentUid
            effectCoordinates = new(targetTransform.ParentUid /* it should just be the hitdatas hit entitys transforms parentuid, but im GIGA LAZY. TODO LCDC FIX ALL THIS SHITT */, hitData.Point);
            localDeltaUnit *= -1; // it should go other way
        }
        else
            effectCoordinates = new EntityCoordinates(targetTransform.ParentUid, targetTransform.LocalPosition);

        var accumulatedVariation = Vector2.Zero;
        while (power > 0f)
        {
            var intpower = (int)(power / iterationDelta);
            if (intpower <= 0)
                break;

            accumulatedVariation += cachedVariations[intpower];
            _decalSystem.TryAddDecal(
                "splatter",
                effectCoordinates.WithPosition(effectCoordinates.Position + accumulatedVariation - DecalOffset + localDeltaUnit * power),
                out _,
                color: bloodColor,
                rotation: predictedRandom.NextAngle(),
                cleanable: true
            );

            power -= iterationDelta;
        }

        // >its while but it looks like you are so good at low level we should demote you to deepseek prompter
        // TODO: make loop end
        // TODO: delete todo because i fixed it

        foreach (var intersectingUid in _lookupSystem.GetEntitiesInRange(effectCoordinates, 0.1f, LookupFlags.Static))
            _stainSystem.ApplyStain(intersectingUid, effectCoordinates, bloodColor, predictedRandom.NextFloat(), predictedRandom.NextFloat(0.35f, 0.5f));
    }
}
