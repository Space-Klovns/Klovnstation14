using System.Numerics;
using Content.Shared._KS14.OverlayStains;
using Content.Shared._KS14.Particles;
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
using Robust.Shared.Utility;

namespace Content.Shared._KS14.BloodSpray;

public sealed partial class BloodSpraySystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private SharedDecalSystem _decalSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private StainSystem _stainSystem = default!;
    [Dependency] private EntityLookupSystem _lookupSystem = default!;
    [Dependency] private RayCastSystem _rayCastSystem = default!;
    [Dependency] private KsSharedParticlesSystem _particlesSystem = default!;

    private static readonly QueryFilter StaticQueryFilter = new() { LayerBits = 0L, Flags = QueryFlags.Static, MaskBits = (long)CollisionGroup.Impassable };
    private static readonly Vector2 DecalOffset = Vector2.One / 2; // this is related to texture size of the blood splatter

    private static readonly ProtoId<DecalPrototype>[] DecalIds =
    {
        "splatter",
        "KsSplatter1",
        "KsSplatter2",
        "KsSplatter3"
    };

    private static readonly ProtoId<KsParticlesPrototype> SplatterMistParticles = "KsBloodSplatterMist";
    private static readonly ProtoId<KsParticlesPrototype> SplatterSpriteParticles = "KsBloodSplatterSprite";
    private static readonly ProtoId<KsParticlesPrototype> GibExplosionParticles = "KsBloodExplosion";
    private static readonly ProtoId<KsParticlesPrototype> GibSplatterParticles = "KsGibSplatter";

    private static readonly SpriteSpecifier.Rsi[] Textures =
    {
        new(new ResPath("/Textures/Effects/crayondecals.rsi"), "splatter"),
        new(new ResPath("/Textures/Fluids/splatter.rsi"), "splatter-0"),
        new(new ResPath("/Textures/Fluids/splatter.rsi"), "splatter-1"),
        new(new ResPath("/Textures/Fluids/splatter.rsi"), "splatter-2")
    };

    public void HandleBleedEffects(Entity<BloodstreamComponent?> entity, DamageSpecifier bloodlossSpecifier, EntityUid originUid)
        => HandleBleedEffects(entity, (float)bloodlossSpecifier.GetTotal(), originUid);

    /// <summary>
    ///     Sprays blood away from <paramref name="originUid"/>, whatever caused the bleeding.
    /// </summary>
    public void HandleBleedEffects(Entity<BloodstreamComponent?> entity, float bloodloss, EntityUid originUid)
    {
        if (!Resolve(entity, ref entity.Comp) ||
            TerminatingOrDeleted(originUid))
            return;

        var worldDeltaUnit = _transformSystem.GetWorldPosition(entity.Owner) - _transformSystem.GetWorldPosition(originUid);

        Vector2Helpers.Normalize(ref worldDeltaUnit);
        HandleBleedEffects(entity!, bloodloss, worldDeltaUnit);
    }

    /// <summary>
    ///     Sprays blood from <paramref name="entity"/> along <paramref name="worldDeltaUnit"/>, splattering decals on the
    ///         floor and staining whatever wall stops the spray.
    /// </summary>
    /// <remarks>
    ///     Everything here is worked out in the frame of the broadphase the entity is on (its grid, or its map
    ///         if it is in space): that is the frame the ray is cast in, the frame its hit point comes back in,
    ///         and the frame the decals are placed in.
    /// </remarks>
    public void HandleBleedEffects(Entity<BloodstreamComponent> entity, float bloodloss, Vector2 worldDeltaUnit)
    {
        if (bloodloss <= 5f)
            return;

        if (!_solutionContainerSystem.ResolveSolution(entity.Owner, entity.Comp.BloodSolutionName, ref entity.Comp.BloodSolution, out var bloodSolution))
            return;

        // TODO: fix occasional mispredict here
        var predictedRandom = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed((int)_gameTiming.CurTick.Value, KsSharedRandomExtensions.GetNetId(entity.Owner, EntityManager));

        var targetTransform = Transform(entity);
        if ((targetTransform.GridUid ?? targetTransform.MapUid) is not { } broadphaseUid)
            return;

        var broadphaseInvWorldMatrix = _transformSystem.GetInvWorldMatrix(broadphaseUid);
        var localPosition = Vector2.Transform(_transformSystem.GetWorldPosition(targetTransform), broadphaseInvWorldMatrix);
        // `Transform` instead of `TransformNormal` would apply translation, which a direction must not have.
        var localDeltaUnit = Vector2.TransformNormal(worldDeltaUnit, broadphaseInvWorldMatrix);

        var bloodColor = bloodSolution.GetColor(ProtoMan);
        SpawnSplatterParticles(entity.Owner, bloodColor, worldDeltaUnit);

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

        // Casting against one broadphase takes and returns positions local to that broadphase.
        RayResult rayResult = new();
        _rayCastSystem.CastRayClosest(
            broadphaseUid,
            ref rayResult,
            localPosition,
            localDeltaUnit * power + totalVariation,
            StaticQueryFilter
        );

        EntityCoordinates effectCoordinates;
        if (rayResult.Hit)
        {
            effectCoordinates = new(broadphaseUid, rayResult.Results[0].Point);
            localDeltaUnit *= -1; // splatter back off the wall, rather than into it
        }
        else
            effectCoordinates = new(broadphaseUid, localPosition);

        var accumulatedVariation = Vector2.Zero;
        while (power > 0f)
        {
            var intpower = (int)(power / iterationDelta);
            if (intpower <= 0)
                break;

            accumulatedVariation += cachedVariations[intpower];
            _decalSystem.TryAddDecal(
                predictedRandom.Pick(DecalIds),
                effectCoordinates.WithPosition(effectCoordinates.Position + accumulatedVariation - DecalOffset + localDeltaUnit * power),
                out _,
                color: bloodColor,
                rotation: predictedRandom.NextAngle(),
                cleanable: true
            );

            power -= iterationDelta;
        }

        foreach (var intersectingUid in _lookupSystem.GetEntitiesInRange(effectCoordinates, 0.1f, LookupFlags.Static))
            _stainSystem.ApplyStain(intersectingUid, effectCoordinates, bloodColor, predictedRandom.NextFloat(), predictedRandom.NextFloat(0.35f, 0.5f), texture: predictedRandom.Pick(Textures));
    }

    /// <summary>
    ///     A hit's spray of blood, TGMC's bloodsplatter: a puff of mist and a splatter, both flying out along
    ///         <paramref name="worldDeltaUnit"/>. Server only; the client is sent the emitters.
    /// </summary>
    private void SpawnSplatterParticles(EntityUid uid, Color bloodColor, Vector2 worldDeltaUnit)
    {
        // The prototypes spray along their emitter's -y, which FromWorldVec turns onto the direction.
        var worldRotation = Angle.FromWorldVec(worldDeltaUnit);
        SpawnBloodParticles(SplatterMistParticles, uid, bloodColor, worldRotation);
        SpawnBloodParticles(SplatterSpriteParticles, uid, bloodColor, worldRotation);
    }

    /// <summary>
    ///     A burst of blood and gore, TGMC's gib_particles, at <paramref name="entity"/> in its blood colour: for a
    ///         gibbing or a lost limb. Does nothing for something without blood, or on the client.
    /// </summary>
    public void SpawnGibParticles(Entity<BloodstreamComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, logMissing: false) ||
            !_solutionContainerSystem.ResolveSolution(entity.Owner, entity.Comp.BloodSolutionName, ref entity.Comp.BloodSolution, out var bloodSolution))
            return;

        var bloodColor = bloodSolution.GetColor(ProtoMan);
        SpawnBloodParticles(GibExplosionParticles, entity.Owner, bloodColor, worldRotation: null);
        SpawnBloodParticles(GibSplatterParticles, entity.Owner, bloodColor, worldRotation: null);
    }

    /// <summary>
    ///     Spawns a one-shot emitter where <paramref name="uid"/> stands, rather than on it, so it stays behind
    ///         when the mob moves or is deleted, as TGMC's temp_visuals do.
    /// </summary>
    private void SpawnBloodParticles(ProtoId<KsParticlesPrototype> prototype, EntityUid uid, Color bloodColor, Angle? worldRotation)
    {
        if (_particlesSystem.SpawnPvs(prototype, Transform(uid).Coordinates) is not { } emitterUid)
            return;

        _particlesSystem.SetColor(emitterUid, bloodColor);
        if (worldRotation is not { } rotation)
            return;

        // The emitter is square to its grid (the prototypes leave followRotation off), so aim it in the grid's frame.
        var gridRotation = Transform(emitterUid).GridUid is { } gridUid ? _transformSystem.GetWorldRotation(gridUid) : Angle.Zero;
        _particlesSystem.SetRotation(emitterUid, rotation - gridRotation);
    }
}
