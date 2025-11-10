// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using System.Runtime.CompilerServices;
using Content.Shared._KS14.Random.Helpers;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Shared._KS14.Sparks;

// TODO: default soundcollection
public abstract class SharedSparksSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;

    public static readonly EntProtoId DefaultSparkPrototype = "EffectSparkMoving";
    public static readonly SoundSpecifier DefaultSoundSpecifier = new SoundCollectionSpecifier("sparks");

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
        in EntityCoordinates coordinates,
        in EntProtoId sparkPrototype,
        SoundSpecifier? soundSpecifier = null,
        int minimumSparks = 2,
        int maximumSparks = 5,
        float minimumSparkVelocity = 1.25f,
        float maximumSparkVelocity = 3f,
        in Entity<MetaDataComponent?>? user = null)
    {
        var random = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed((int)_gameTiming.CurTick.Value, (int)coordinates.Position.LengthSquared());

        var sparks = random.Next(minimumSparks, maximumSparks);
        if (sparks <= 0)
            return;

        for (var i = 0; i < sparks; ++i)
            DoSpark(coordinates, sparkPrototype, null, minimumSparkVelocity, maximumSparkVelocity, user, random);

        if (soundSpecifier is { })
            _audioSystem.PlayPredicted(soundSpecifier, coordinates, user);
    }

    /// <summary>
    ///     Spawns a random number of sparks attached to a position, each launched in a random direction at a random velocity.
    ///         Plays a soundcollection (<see cref="DefaultSoundSpecifier"/>) and spawns the default entity prototype, being <see cref="DefaultSparkPrototype"/>. 
    /// </summary>
    public void DoSparks(
        in EntityCoordinates coordinates,
        int minimumSparks = 2,
        int maximumSparks = 5,
        float minimumSparkVelocity = 1.25f,
        float maximumSparkVelocity = 3f,
        in Entity<MetaDataComponent?>? user = null)
    {
        DoSparks(coordinates, DefaultSparkPrototype, DefaultSoundSpecifier, minimumSparks, maximumSparks, minimumSparkVelocity, maximumSparkVelocity, user);
    }

    /// <summary>
    ///     Spawns a single spark attached to a position, and launches it in a random direction at a random velocity.
    ///         Optionally also plays a sound at the given position.
    /// </summary>
    /// <param name="random">Random used to get velocity and direction of the spark. Should have a predicted seed if this method is being used in prediction.</param>
    /// <returns>The spawned entity.</returns>
    public EntityUid DoSpark(
        in EntityCoordinates coordinates,
        in EntProtoId sparkPrototype,
        SoundSpecifier? soundSpecifier = null,
        float minimumVelocity = 2.5f,
        float maximumVelocity = 5f,
        in Entity<MetaDataComponent?>? user = null,
        System.Random? random = null
    )
    {
        var spark = EntityManager.PredictedSpawn(sparkPrototype);
        random ??= KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(
            (int)_gameTiming.CurTick.Value,
            KsSharedRandomExtensions.GetNetId(spark, EntityManager),
            user != null ? KsSharedRandomExtensions.GetNetId(user.Value, EntityManager) : 0
        );

        // now, spawn in random direction at random velocity between given minimum/maximum velocity
        _transformSystem.SetCoordinates(spark, coordinates);
        var sparkDirectionVector = new Angle(random.NextFloat() * MathF.Tau).ToWorldVec();
        _physicsSystem.SetLinearVelocity(spark, sparkDirectionVector * random.NextFloat(minimumVelocity, maximumVelocity));

        if (soundSpecifier is { })
            _audioSystem.PlayPredicted(soundSpecifier, coordinates, user);

        return spark;
    }

    /// <summary>
    ///     Spawns a single spark at a position, and launches it in a given direction at a given velocity.
    /// </summary>
    /// <returns>The spawned entity.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityUid SpawnSpark(in MapCoordinates coordinates, in Vector2 velocityVector, in EntProtoId sparkPrototype)
    {
        var spark = EntityManager.PredictedSpawn(sparkPrototype, coordinates);
        _physicsSystem.SetLinearVelocity(spark, velocityVector);

        return spark;
    }

    /// <inheritdoc cref="SpawnSpark(MapCoordinates, Vector2, EntProtoId)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityUid SpawnSpark(in MapCoordinates coordinates, in Angle direction, float velocityScalar, in EntProtoId sparkPrototype)
        => SpawnSpark(coordinates, direction.ToWorldVec() * velocityScalar, sparkPrototype);

    /// <summary>
    ///     Spawns a single spark attached to a position, and launches it in a given direction at a given velocity.
    /// </summary>
    /// <returns>The spawned entity.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityUid SpawnSparkAttached(in EntityCoordinates coordinates, in Vector2 velocityVector, in EntProtoId sparkPrototype)
    {
        var spark = EntityManager.PredictedSpawnAttachedTo(sparkPrototype, coordinates);
        _physicsSystem.SetLinearVelocity(spark, velocityVector);

        return spark;
    }

    /// <inheritdoc cref="SpawnSparkAttached(EntityCoordinates, Vector2, EntProtoId)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityUid SpawnSparkAttached(in EntityCoordinates coordinates, in Angle direction, float velocityScalar, in EntProtoId sparkPrototype)
        => SpawnSparkAttached(coordinates, direction.ToWorldVec() * velocityScalar, sparkPrototype);
}
