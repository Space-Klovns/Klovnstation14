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
using Robust.Shared.Network;
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
    [Dependency] private INetManager _netManager = default!;

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
    private static readonly ProtoId<KsParticlesPrototype> DismemberExplosionParticles = "KsDismemberBloodExplosion";
    private static readonly ProtoId<KsParticlesPrototype> DismemberSplatterParticles = "KsDismemberGibSplatter";

    /// <summary>
    ///     The limb throw speed, in metres per second, at which a dismemberment's burst flies at its prototype's own
    ///         speed. Faster throws spray faster, up to <see cref="MaxDismemberSpeedMultiplier"/> times.
    /// </summary>
    public const float DismemberReferenceThrowSpeed = 10f;

    public const float MinDismemberSpeedMultiplier = 0.5f;
    public const float MaxDismemberSpeedMultiplier = 2f;

    /// <summary>
    ///     The damage of a hit whose spray is the prototypes' own size. A hit of twice this sprays twice the mist,
    ///         between <see cref="MinSprayAmount"/> and <see cref="MaxSprayAmount"/> times.
    /// </summary>
    public const float ReferenceSprayDamage = 15f;

    public const float MinSprayAmount = 0.3f;
    public const float MaxSprayAmount = 3f;

    /// <summary>
    ///     How many ticks a client remembers where a predicted hit bled from: more than it ever re-predicts.
    /// </summary>
    private const int PredictedHitOriginTicks = 256;

    /// <summary>
    ///     Where a hit on <see cref="Target"/> predicted on <see cref="Tick"/> bled from, in the frame of
    ///         <see cref="Broadphase"/>. A transient cache on the client, pruned by tick, not per-entity state.
    /// </summary>
    private record struct PredictedHitOrigin(EntityUid Target, GameTick Tick, EntityUid Broadphase, Vector2 LocalPosition, Vector2 LocalDeltaUnit);

    private readonly List<PredictedHitOrigin> _predictedHitOrigins = new();

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
    /// <param name="damage">
    ///     The damage the hit dealt, which sizes the spray of particles. Null sizes it by <paramref name="bloodloss"/>.
    /// </param>
    public void HandleBleedEffects(Entity<BloodstreamComponent?> entity, float bloodloss, EntityUid originUid, float? damage = null)
    {
        if (!Resolve(entity, ref entity.Comp) ||
            TerminatingOrDeleted(originUid))
            return;

        var worldDeltaUnit = _transformSystem.GetWorldPosition(entity.Owner) - _transformSystem.GetWorldPosition(originUid);

        Vector2Helpers.Normalize(ref worldDeltaUnit);
        HandleBleedEffects(entity!, bloodloss, worldDeltaUnit, damage: damage);
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
    /// <param name="damage">
    ///     The damage the hit dealt, which sizes the spray of particles. Null sizes it by <paramref name="bloodloss"/>.
    /// </param>
    public void HandleBleedEffects(Entity<BloodstreamComponent> entity, float bloodloss, Vector2 worldDeltaUnit, float? damage = null)
    {
        if (bloodloss <= 5f)
            return;

        if (!_solutionContainerSystem.ResolveSolution(entity.Owner, entity.Comp.BloodSolutionName, ref entity.Comp.BloodSolution, out var bloodSolution))
            return;

        // TODO: fix occasional mispredict here
        var predictedRandom = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed((int)_gameTiming.CurTick.Value, KsSharedRandomExtensions.GetNetId(entity.Owner, EntityManager));

        if (!TryResolveHitOrigin(entity.Owner, worldDeltaUnit, out var broadphaseUid, out var localPosition, out var localDeltaUnit))
            return;

        var bloodColor = bloodSolution.GetColor(ProtoMan);
        var sprayAmount = Math.Clamp((damage ?? bloodloss) / ReferenceSprayDamage, MinSprayAmount, MaxSprayAmount);
        SpawnSplatterParticles(entity.Owner, new EntityCoordinates(broadphaseUid, localPosition), bloodColor, localDeltaUnit, sprayAmount);

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
    ///     Where the hit on <paramref name="targetUid"/> bleeds from, in the frame of the broadphase it is on (its
    ///         grid, or its map in space): that is the frame the ray is cast in, the frame its hit point comes back
    ///         in, and the frame the decals are placed in.
    /// </summary>
    /// <remarks>
    ///     A client re-predicts a hit's tick after every server state, with the target wherever that state put it.
    ///         Worked out afresh each time, a moving target's predicted decals would follow it until the server's
    ///         arrived. So the client keeps the origin of the tick's first prediction and reuses it.
    /// </remarks>
    private bool TryResolveHitOrigin(EntityUid targetUid, Vector2 worldDeltaUnit, out EntityUid broadphaseUid, out Vector2 localPosition, out Vector2 localDeltaUnit)
    {
        var tick = _gameTiming.CurTick;
        if (_netManager.IsClient)
        {
            _predictedHitOrigins.RemoveAll(origin => origin.Tick.Value + PredictedHitOriginTicks < tick.Value);
            foreach (var origin in _predictedHitOrigins)
            {
                if (origin.Target != targetUid || origin.Tick != tick || TerminatingOrDeleted(origin.Broadphase))
                    continue;

                broadphaseUid = origin.Broadphase;
                localPosition = origin.LocalPosition;
                localDeltaUnit = origin.LocalDeltaUnit;
                return true;
            }
        }

        localPosition = default;
        localDeltaUnit = default;
        var targetTransform = Transform(targetUid);
        if ((targetTransform.GridUid ?? targetTransform.MapUid) is not { } resolvedBroadphaseUid)
        {
            broadphaseUid = default;
            return false;
        }

        broadphaseUid = resolvedBroadphaseUid;
        var broadphaseInvWorldMatrix = _transformSystem.GetInvWorldMatrix(broadphaseUid);
        localPosition = Vector2.Transform(_transformSystem.GetWorldPosition(targetTransform), broadphaseInvWorldMatrix);
        // `Transform` instead of `TransformNormal` would apply translation, which a direction must not have.
        localDeltaUnit = Vector2.TransformNormal(worldDeltaUnit, broadphaseInvWorldMatrix);

        if (_netManager.IsClient)
            _predictedHitOrigins.Add(new PredictedHitOrigin(targetUid, tick, broadphaseUid, localPosition, localDeltaUnit));

        return true;
    }

    /// <summary>
    ///     A hit's spray of blood, TGMC's bloodsplatter: a puff of mist and a splatter at <paramref name="coordinates"/>,
    ///         both flying out along <paramref name="localDeltaUnit"/>, a direction in the frame of the coordinates'
    ///         parent. Predicted: the client that predicted the hit shows its own at once. <paramref name="amount"/>
    ///         multiplies the mist; the single splatter grows with its square root.
    /// </summary>
    private void SpawnSplatterParticles(EntityUid uid, EntityCoordinates coordinates, Color bloodColor, Vector2 localDeltaUnit, float amount)
    {
        // The prototypes spray along their emitter's -y, which FromWorldVec turns onto the direction.
        var localRotation = Angle.FromWorldVec(localDeltaUnit);
        SpawnBloodParticles(SplatterMistParticles, uid, coordinates, bloodColor, localRotation, speed: 1f, amount: amount);
        SpawnBloodParticles(SplatterSpriteParticles, uid, coordinates, bloodColor, localRotation, speed: 1f, scale: MathF.Sqrt(amount));
    }

    /// <summary>
    ///     A burst of blood and gore, TGMC's gib_particles, at <paramref name="entity"/> in its blood colour, every way
    ///         at once: for a gibbing. Does nothing for something without blood.
    /// </summary>
    public void SpawnGibParticles(Entity<BloodstreamComponent?> entity)
    {
        if (!TryGetBloodColor(entity, out var bloodColor))
            return;

        var coordinates = Transform(entity.Owner).Coordinates;
        SpawnBloodParticles(GibExplosionParticles, entity.Owner, coordinates, bloodColor, localRotation: null, speed: 1f);
        SpawnBloodParticles(GibSplatterParticles, entity.Owner, coordinates, bloodColor, localRotation: null, speed: 1f);
    }

    /// <summary>
    ///     The gib burst for a lost limb: thrown after the limb, along <paramref name="worldDirection"/>, faster the
    ///         harder it was thrown (<paramref name="throwSpeed"/>, metres per second), within limits.
    /// </summary>
    public void SpawnDismemberParticles(Entity<BloodstreamComponent?> entity, Vector2 worldDirection, float throwSpeed)
    {
        if (worldDirection.LengthSquared() <= float.Epsilon)
        {
            SpawnGibParticles(entity);
            return;
        }

        if (!TryGetBloodColor(entity, out var bloodColor))
            return;

        var transformComponent = Transform(entity.Owner);
        // The emitter goes on the mob's parent (its grid), so the throw is turned into that frame.
        var parentRotation = transformComponent.ParentUid.IsValid() ? _transformSystem.GetWorldRotation(transformComponent.ParentUid) : Angle.Zero;
        var localRotation = Angle.FromWorldVec(Vector2.Normalize(worldDirection)) - parentRotation;
        var speed = Math.Clamp(throwSpeed / DismemberReferenceThrowSpeed, MinDismemberSpeedMultiplier, MaxDismemberSpeedMultiplier);
        SpawnBloodParticles(DismemberExplosionParticles, entity.Owner, transformComponent.Coordinates, bloodColor, localRotation, speed);
        SpawnBloodParticles(DismemberSplatterParticles, entity.Owner, transformComponent.Coordinates, bloodColor, localRotation, speed);
    }

    private bool TryGetBloodColor(Entity<BloodstreamComponent?> entity, out Color bloodColor)
    {
        bloodColor = default;
        if (!Resolve(entity, ref entity.Comp, logMissing: false) ||
            !_solutionContainerSystem.ResolveSolution(entity.Owner, entity.Comp.BloodSolutionName, ref entity.Comp.BloodSolution, out var bloodSolution))
            return false;

        bloodColor = bloodSolution.GetColor(ProtoMan);
        return true;
    }

    /// <summary>
    ///     Spawns a one-shot emitter at <paramref name="coordinates"/>, on the ground rather than on the mob, so it
    ///         stays behind when the mob moves or is deleted, as TGMC's temp_visuals do. <paramref name="localRotation"/>
    ///         aims the prototype's -y in the frame of the coordinates' parent; <paramref name="speed"/> multiplies its
    ///         velocities, <paramref name="amount"/> how many particles spawn and <paramref name="scale"/> the whole
    ///         effect. <paramref name="sourceUid"/> is the mob it is about, which tells a predicted spawn's
    ///         re-predictions apart from a second spawn.
    /// </summary>
    private void SpawnBloodParticles(ProtoId<KsParticlesPrototype> prototype, EntityUid sourceUid, EntityCoordinates coordinates, Color bloodColor, Angle? localRotation, float speed, float amount = 1f, float scale = 1f)
    {
        if (_particlesSystem.SpawnPredictedEffect(prototype, coordinates, sourceUid) is not { } emitterUid)
            return;

        _particlesSystem.SetColor(emitterUid, bloodColor);
        _particlesSystem.SetSpeed(emitterUid, speed);
        _particlesSystem.SetAmount(emitterUid, amount);
        _particlesSystem.SetScale(emitterUid, scale);
        if (localRotation is not { } rotation)
            return;

        // The emitter is square to its grid (the prototypes leave followRotation off). Its parent is that grid, or
        //      the map in space, so a rotation in the parent's frame is already in the emitter's.
        _particlesSystem.SetRotation(emitterUid, rotation);
    }
}
