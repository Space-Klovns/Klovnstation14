using System.Numerics;
using Content.Shared._KS14.OverlayStains;
using Content.Shared._KS14.Random.Helpers;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
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

    private static readonly Vector2 DecalOffset = Vector2.One / 2; // KS14 Addition; this is related to texture size of the blood splatter.

    public void HandleBleedEffects(in Entity<BloodstreamComponent> entity, in DamageChangedEvent args, DamageSpecifier bloodlossSpecifier)
    {
        if (args.Origin is not { } originUid)
            return;

        var bloodloss = (float)bloodlossSpecifier.GetTotal();
        if (bloodloss <= 0.5f)
            return;

        var targetTransform = Transform(entity);
        var originTransform = Transform(originUid);

        var worldDeltaUnit = _transformSystem.GetWorldPosition(targetTransform) - _transformSystem.GetWorldPosition(originTransform);

        Vector2Helpers.Normalize(ref worldDeltaUnit);
        HandleBleedEffects(entity, bloodloss, _transformSystem.GetWorldPosition(targetTransform), targetTransform, originTransform.ParentUid, worldDeltaUnit, originUid: args.Origin);
    }

    // - [x] Tested, works.
    // TODO: Clean up code
    // Coder's Ultimatum
    // TODO LCDC: FUCKING FIX THIS HOLY SHIT! IITS SO EASY
    public void HandleBleedEffects(Entity<BloodstreamComponent> entity, float bloodloss, Vector2 targetWorldPosition, TransformComponent targetTransform, EntityUid parentUid, Vector2 worldDeltaUnit, EntityUid? originUid = null)
    {
        // it shouldnt be 0 ever anyway
        if (bloodloss <= 0.5f)
            return;

        if (!_solutionContainerSystem.ResolveSolution(entity.Owner, entity.Comp.BloodSolutionName, ref entity.Comp.BloodSolution, out var bloodSolution))
            return;

        // TODO: fix occasional mispredict here
        var predictedRandom = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed((int)_gameTiming.CurTick.Value, (int)targetTransform.LocalPosition.LengthSquared());
        var parentInvWorldMatrix = _transformSystem.GetInvWorldMatrix(parentUid);

        var bloodColor = bloodSolution.GetColor(_prototypeManager);
        bloodColor = bloodColor.WithAlpha(bloodColor.A * predictedRandom.NextFloat(0.28f, 0.2206761f)); // random alpha

        const float maxPower = 1.75f;
        var power = MathF.Max(maxPower * (1f - MathF.Exp(-bloodloss / 6f)), 0f);

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

        RayResult rayResult = new();
        _rayCastSystem.CastRayClosest(
            parentUid,
            ref rayResult,
            targetTransform.LocalPosition,
            worldDeltaUnit * power + totalVariation,
            new QueryFilter() { LayerBits = 1L, Flags = QueryFlags.Static, MaskBits = (long)CollisionGroup.Impassable }
        );

        EntityCoordinates effectCoordinates;
        if (rayResult.Hit)
        {
            var hitData = rayResult.Results[0];
            EntityUid hitParentUid;

            // handle cross-grid
            if (hitData.Entity == entity.Owner)
                hitParentUid = targetTransform.ParentUid;
            else if (originUid != null && hitData.Entity == originUid)
                hitParentUid = parentUid;
            else
                hitParentUid = Transform(hitData.Entity).ParentUid;

            // docs for RayHit lie because RayHit.Point isnt the *caller* changing it to local terms, but instead the code constructing it; it is also local to the hit entity's parent(? TODO: confirm that assumption)
            // tldr `hitData.Point` is local to `hitData.Entity`'s ParentUid
            effectCoordinates = new(hitParentUid, hitData.Point);
        }
        else
            effectCoordinates = new EntityCoordinates(targetTransform.ParentUid, targetTransform.LocalPosition/*Vector2.Transform(targetWorldPosition + worldDeltaUnit * power, parentInvWorldMatrix)*/);

        var accumulatedVariation = Vector2.Zero;
        while (power > 0f)
        {
            var intpower = (int)(power / iterationDelta);
            if (intpower <= 0)
                break;

            accumulatedVariation += cachedVariations[intpower];
            _decalSystem.TryAddDecal(
                "splatter",
                effectCoordinates.WithPosition(effectCoordinates.Position + accumulatedVariation - DecalOffset - worldDeltaUnit * power),
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
