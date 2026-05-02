using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Shared._KS14.PredictedSpawning;

// TODO LCDC: MAKE THIS ACTUALLY WORK GEEEG

/// <summary>
///     Contains replacements for <see cref="EntityManager.PredictedSpawn(string?, Robust.Shared.Prototypes.ComponentRegistry?, bool)"/>.
/// </summary>
public abstract class KsSharedPredictedSpawnSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        _physicsQuery = GetEntityQuery<PhysicsComponent>();
    }

    /// <remarks>
    ///     Does predicted spawn as usual, while predicting physics too.
    ///         If specified user is notnull,
    /// </remarks>
    public EntityUid PredictedSpawn(string entityProtoId, ComponentRegistry? componentOverrides = null, bool doMapInit = false)
        => FlagPredictedAndReturn(EntityManager.PredictedSpawn(entityProtoId, overrides: componentOverrides, doMapInit: doMapInit));

    /// <inheritdoc cref="PredictedSpawn(string, ComponentRegistry?, bool)"/>
    public EntityUid PredictedSpawn(string entityProtoId, MapCoordinates coordinates, ComponentRegistry? componentOverrides = null, Angle rotation = default)
        => FlagPredictedAndReturn(EntityManager.PredictedSpawn(entityProtoId, coordinates, overrides: componentOverrides, rotation: rotation));

    /// <inheritdoc cref="PredictedSpawn(string, ComponentRegistry?, bool)"/>
    public new EntityUid PredictedSpawnAttachedTo(string entityProtoId, EntityCoordinates coordinates, ComponentRegistry? componentOverrides = null, Angle rotation = default)
        => FlagPredictedAndReturn(PredictedSpawnAttachedTo(entityProtoId, coordinates, overrides: componentOverrides, rotation: rotation));

    /// <summary>
    ///     Flags the given entity as predicted.
    /// </summary>
    /// <returns>Same <see cref="EntityUid"/> as the one provided, so that some method definitions can be one-lined.</returns>
    private EntityUid FlagPredictedAndReturn(EntityUid uid)
    {
        if (_physicsQuery.HasComponent(uid))
            _physicsSystem.UpdateIsPredicted(uid);

        return uid;
    }
}
