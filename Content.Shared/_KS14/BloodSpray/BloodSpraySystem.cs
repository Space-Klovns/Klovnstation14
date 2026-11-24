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
    private static readonly ProtoId<KsParticlesPrototype> ExitWoundMistParticles = "KsExitWoundMist";
    private static readonly ProtoId<KsParticlesPrototype> SplatterSpriteParticles = "KsBloodSplatterSprite";
    private static readonly ProtoId<KsParticlesPrototype> GibExplosionParticles = "KsBloodExplosion";
    private static readonly ProtoId<KsParticlesPrototype> GibSplatterParticles = "KsGibSplatter";
    private static readonly ProtoId<KsParticlesPrototype> DismemberExplosionParticles = "KsDismemberBloodExplosion";
    private static readonly ProtoId<KsParticlesPrototype> DismemberSplatterParticles = "KsDismemberGibSplatter";
    private static readonly ProtoId<KsParticlesPrototype> HeavyHitExplosionParticles = "KsHeavyHitBloodExplosion";
    private static readonly ProtoId<KsParticlesPrototype> HeavyHitSplatterParticles = "KsHeavyHitGibSplatter";

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
    ///     A hit of at least this much damage is a heavy one: its mist flies out faster, and the exit wound throws
    ///         a few puffs of blood and bits of gore after it, more the harder the hit. A pistol round is under it.
    /// </summary>
    public const float HeavyHitDamage = 38f;

    /// <summary>
    ///     How much faster a heavy hit's mist flies per damage past <see cref="HeavyHitDamage"/>, up to
    ///         <see cref="MaxHeavySpraySpeed"/>.
    /// </summary>
    public const float HeavySpraySpeedPerDamage = 1f / 40f;

    public const float MaxHeavySpraySpeed = 1.8f;

    /// <summary>
    ///     A heavy hit's gore burst (<c>KsHeavyHitBloodExplosion</c>, <c>KsHeavyHitGibSplatter</c>), as a share of the
    ///         lost limb's it is made from: this much per damage, between the min and the max. Their own prototypes,
    ///         so a hit that also takes a limb off in the same tick still shows both (predicted effects are told apart
    ///         by prototype, source and tick).
    /// </summary>
    public const float HeavyGorePerDamage = 1f / 300f;

    public const float MinHeavyGoreAmount = 0.1f;
    public const float MaxHeavyGoreAmount = 0.3f;

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

    /// <summary>
    ///     What seeds the rolls of the blood sprayed inside the open <see cref="BeginHit"/>, in place of the tick.
    /// </summary>
    private int? _hitSeed;

    /// <summary>
    ///     Whether the blood sprayed inside the open <see cref="BeginHit"/> is for the hit of a client's kept copy of its
    ///         own shot, which is never run again and must leave networked state alone.
    /// </summary>
    private bool _keptHit;

    /// <summary>
    ///     Opens a scope in which the blood a hit sprays is rolled from <paramref name="seed"/>, a number the client that
    ///         predicts the hit and the server both have (a bullet's shot), rather than from the tick each of them
    ///         happens to run it on.
    /// </summary>
    /// <param name="seed">Null rolls from the tick, as outside any scope.</param>
    /// <param name="kept">Whether this is the hit of a client's kept copy of its own shot.</param>
    public HitScope BeginHit(int? seed, bool kept)
    {
        var scope = new HitScope(this, _hitSeed, _keptHit);
        _hitSeed = seed;
        _keptHit = kept;
        return scope;
    }

    /// <summary>
    ///     Restores the scope before it when disposed. See <see cref="BeginHit"/>.
    /// </summary>
    public readonly struct HitScope : IDisposable
    {
        private readonly BloodSpraySystem _system;
        private readonly int? _previousSeed;
        private readonly bool _previousKept;

        public HitScope(BloodSpraySystem system, int? previousSeed, bool previousKept)
        {
            _system = system;
            _previousSeed = previousSeed;
            _previousKept = previousKept;
        }

        public void Dispose()
        {
            _system._hitSeed = _previousSeed;
            _system._keptHit = _previousKept;
        }
    }

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

        // The same rolls on the client that predicts the hit and on the server. By the tick, for what both run on the same
        //      one (a predicted swing or hitscan shot). A bullet's hit lands on different ticks on each, and is seeded by
        //      its shot instead (BeginHit), or the stains the shooter saw changed when the server's arrived.
        var predictedRandom = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(_hitSeed ?? (int)_gameTiming.CurTick.Value, KsSharedRandomExtensions.GetNetId(entity.Owner, EntityManager));

        if (!TryResolveHitOrigin(entity.Owner, worldDeltaUnit, out var broadphaseUid, out var localPosition, out var localDeltaUnit))
            return;

        var bloodColor = bloodSolution.GetColor(ProtoMan);
        var sprayAmount = Math.Clamp((damage ?? bloodloss) / ReferenceSprayDamage, MinSprayAmount, MaxSprayAmount);
        SpawnSplatterParticles(entity.Owner, new EntityCoordinates(broadphaseUid, localPosition), bloodColor, localDeltaUnit, sprayAmount, damage ?? 0f);

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
            var decalId = predictedRandom.Pick(DecalIds);
            var decalCoordinates = effectCoordinates.WithPosition(effectCoordinates.Position + accumulatedVariation - DecalOffset + localDeltaUnit * power);
            var decalRotation = predictedRandom.NextAngle();

            // A kept copy's hit is never run again, so a decal written into the networked chunk would be gone with the
            //      next state: it is kept apart from it until the server's arrives.
            if (_keptHit)
                _decalSystem.KsAddKeptPredictedDecal(decalId, decalCoordinates, color: bloodColor, rotation: decalRotation, cleanable: true);
            else
                _decalSystem.TryAddDecal(decalId, decalCoordinates, out _, color: bloodColor, rotation: decalRotation, cleanable: true);

            power -= iterationDelta;
        }

        // A stain is networked state on what it is on: a kept copy's hit leaves it to the server's.
        // TODO: predict these for a kept copy's hit as the floor decals are (KsAddKeptPredictedDecal): a client-only
        //      stain on the wall, drawn by StainOverlayVisualizerSystem, until the server's StainedComponent state has it.
        //      Until then the shooter sees the blood on a wall a round trip after the blood on the floor.
        if (_keptHit)
            return;

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
    ///         multiplies the mist; the single splatter grows with its square root. A hit of
    ///         <see cref="HeavyHitDamage"/> or more (<paramref name="damage"/>) sprays faster and throws gore too.
    /// </summary>
    private void SpawnSplatterParticles(EntityUid uid, EntityCoordinates coordinates, Color bloodColor, Vector2 localDeltaUnit, float amount, float damage)
    {
        // The prototypes spray along their emitter's -y, which FromWorldVec turns onto the direction.
        var localRotation = Angle.FromWorldVec(localDeltaUnit);
        var mistSpeed = damage >= HeavyHitDamage
            ? Math.Min(1f + (damage - HeavyHitDamage) * HeavySpraySpeedPerDamage, MaxHeavySpraySpeed)
            : 1f;
        SpawnBloodParticles(SplatterMistParticles, uid, coordinates, bloodColor, localRotation, speed: mistSpeed, amount: amount);
        SpawnBloodParticles(SplatterSpriteParticles, uid, coordinates, bloodColor, localRotation, speed: 1f, scale: MathF.Sqrt(amount));

        if (damage < HeavyHitDamage)
            return;

        // A heavy hit punches gore out of the exit wound: a slice of a lost limb's burst, thrown the same way.
        var goreAmount = Math.Clamp(damage * HeavyGorePerDamage, MinHeavyGoreAmount, MaxHeavyGoreAmount);
        SpawnBloodParticles(HeavyHitExplosionParticles, uid, coordinates, bloodColor, localRotation, speed: mistSpeed, amount: goreAmount);
        SpawnBloodParticles(HeavyHitSplatterParticles, uid, coordinates, bloodColor, localRotation, speed: mistSpeed, amount: goreAmount);
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
    ///     A small puff of blood mist out of the exit wound of a hit <paramref name="entity"/>'s armour mostly
    ///         stopped, along <paramref name="worldDirection"/>: what little got through.
    /// </summary>
    public void SpawnExitWoundMist(Entity<BloodstreamComponent?> entity, Vector2 worldDirection)
    {
        if (worldDirection.LengthSquared() <= float.Epsilon || !TryGetBloodColor(entity, out var bloodColor))
            return;

        var transformComponent = Transform(entity.Owner);
        var parentRotation = transformComponent.ParentUid.IsValid() ? _transformSystem.GetWorldRotation(transformComponent.ParentUid) : Angle.Zero;
        var localRotation = Angle.FromWorldVec(Vector2.Normalize(worldDirection)) - parentRotation;
        // Its own prototype: the hit's own spray is the splatter mist from this mob this tick, and a predicted effect is
        //      told apart by those three, so the same one would be dropped as a re-prediction of the other.
        SpawnBloodParticles(ExitWoundMistParticles, entity.Owner, transformComponent.Coordinates, bloodColor, localRotation, speed: 1f, amount: MinSprayAmount);
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
        // A kept copy's hit is run once, so each is a spray of its own: every pellet of a shot hits on the one tick.
        if (_particlesSystem.SpawnPredictedEffect(prototype, coordinates, sourceUid, once: _keptHit) is not { } emitterUid)
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
