// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using Content.Shared.Random.Helpers;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Sparks;

// TODO: default soundcollection
public abstract class SharedSparksSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;

    public static readonly EntProtoId DefaultSparkPrototype = "EffectSparks";

    /// <summary>
    ///     Hotspot-exposes a tile (if any exists) at the given coordinates.
    ///         Does nothing if either on client, or the given <paramref name="coordinates"/>
    ///         are inside an enclosed container.
    /// </summary>
    public abstract void ExposeSpark(EntityCoordinates coordinates, float exposedTemperature, float exposedVolume);

    /// <summary>
    ///     Spawns a random number of sparks attached to a position, each launched in a random direction at a random velocity.
    ///         Optionally also plays a sound at the given position.
    /// </summary>
    public void DoSparks(
        EntityCoordinates coordinates,
        EntProtoId sparkPrototype,
        SoundSpecifier? soundSpecifier = null,
        int minimumSparks = 1,
        int maximumSparks = 3,
        float minimumSparkVelocity = 2.5f,
        float maximumSparkVelocity = 5f,
        Entity<MetaDataComponent?>? user = null)
    {
        var seed = SharedRandomExtensions.HashCodeCombine(new() { (int)_gameTiming.CurTick.Value, (int)coordinates.Position.LengthSquared() });
        var random = new System.Random(seed);

        var sparks = random.Next(minimumSparks, maximumSparks);
        if (sparks <= 0)
            return;

        for (var i = 0; i < sparks; ++i)
            DoSpark(coordinates, sparkPrototype, null, minimumSparkVelocity, maximumSparkVelocity, user, random);

        if (soundSpecifier is { })
            _audioSystem.PlayPredicted(soundSpecifier, coordinates, user);
    }

    /// <summary>
    ///     Spawns a single spark attached to a position, and launches it in a random direction at a random velocity.
    ///         Optionally also plays a sound at the given position.
    /// </summary>
    /// <param name="random">Random used to get velocity and direction of the spark. Should be predicted if this method is being used in prediction.</param>
    /// <returns>The spawned entity.</returns>
    public EntityUid DoSpark(
        EntityCoordinates coordinates,
        EntProtoId sparkPrototype,
        SoundSpecifier? soundSpecifier = null,
        float minimumVelocity = 2.5f,
        float maximumVelocity = 5f,
        Entity<MetaDataComponent?>? user = null,
        System.Random? random = null
    )
    {
        var spark = EntityManager.PredictedSpawn(sparkPrototype);
        if (random == null)
        {
            var seed = SharedRandomExtensions.HashCodeCombine(new() { (int)_gameTiming.CurTick.Value, GetNetEntity(spark).Id, user != null ? GetNetEntity(user.Value, user).Id : 0 });
            random ??= new System.Random(seed);
        }

        // now, spawn in random direction at random velocity between given minimum/maximum velocity
        var sparkDirectionVector = new Angle(random.NextFloat() * MathF.Tau).ToWorldVec();
        _physicsSystem.SetLinearVelocity(spark, sparkDirectionVector * random.NextFloat(minimumVelocity, maximumVelocity));
        _transformSystem.SetCoordinates(spark, coordinates);

        if (soundSpecifier is { })
            _audioSystem.PlayPredicted(soundSpecifier, coordinates, user);

        return spark;
    }

    /// <summary>
    ///     Spawns a single spark at a position, and launches it in a given direction at a given velocity.
    /// </summary>
    /// <returns>The spawned entity.</returns>
    public EntityUid SpawnSpark(MapCoordinates coordinates, Vector2 velocityVector, EntProtoId sparkPrototype)
    {
        var spark = EntityManager.PredictedSpawn(sparkPrototype, coordinates);
        _physicsSystem.SetLinearVelocity(spark, velocityVector);

        return spark;
    }

    /// <inheritdoc cref="SpawnSpark(MapCoordinates, Vector2, EntProtoId)"/>
    public EntityUid SpawnSpark(MapCoordinates coordinates, Angle direction, float velocityScalar, EntProtoId sparkPrototype)
        => SpawnSpark(coordinates, direction.ToWorldVec() * velocityScalar, sparkPrototype);

    /// <summary>
    ///     Spawns a single spark attached to a position, and launches it in a given direction at a given velocity.
    /// </summary>
    /// <returns>The spawned entity.</returns>
    public EntityUid SpawnSparkAttached(EntityCoordinates coordinates, Vector2 velocityVector, EntProtoId sparkPrototype)
    {
        var spark = EntityManager.PredictedSpawnAttachedTo(sparkPrototype, coordinates);
        _physicsSystem.SetLinearVelocity(spark, velocityVector);

        return spark;
    }

    /// <inheritdoc cref="SpawnSparkAttached(EntityCoordinates, Vector2, EntProtoId)"/>
    public EntityUid SpawnSparkAttached(EntityCoordinates coordinates, Angle direction, float velocityScalar, EntProtoId sparkPrototype)
        => SpawnSparkAttached(coordinates, direction.ToWorldVec() * velocityScalar, sparkPrototype);
}
