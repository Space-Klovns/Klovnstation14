// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.PredictedSpawning;

// TODO LCDC: MAKE THIS ACTUALLY WORK GEEEG

/// <summary>
///     Contains replacements for <see cref="EntityManager.PredictedSpawn(string?, Robust.Shared.Prototypes.ComponentRegistry?, bool)"/>
///         and it's million variants, because RT's implementation is very bad. 
/// </summary>
public abstract class KsSharedPredictedSpawnSystem : EntitySystem
{
    /// <remarks>
    ///     This has a very slight overhead of adding <see cref="KsPredictedSpawnComponent"/>
    ///         to the entity.
    /// </remarks>
    // public EntityUid PredictedSpawn(string entityProtoId, ComponentRegistry? componentOverrides = null, bool doMapInit = false)
    //     => FlagPredictedAndReturn(Spawn(entityProtoId, overrides: componentOverrides, doMapInit: doMapInit));

    // TODO LCDC: FIX THIS SHIT
    public EntityUid PredictedSpawn(string entityProtoId, ComponentRegistry? componentOverrides = null, bool doMapInit = false)
        => EntityManager.PredictedSpawn(entityProtoId, overrides: componentOverrides, doMapInit: doMapInit);

    /// <inheritdoc cref="PredictedSpawn(string, ComponentRegistry?, bool)"/>
    // public EntityUid PredictedSpawn(string entityProtoId, MapCoordinates coordinates, ComponentRegistry? componentOverrides = null, Angle rotation = default)
    //     => FlagPredictedAndReturn(Spawn(entityProtoId, coordinates, overrides: componentOverrides, rotation: rotation));

    // TODO LCDC: FIX THIS SHIT
    public EntityUid PredictedSpawn(string entityProtoId, MapCoordinates coordinates, ComponentRegistry? componentOverrides = null, Angle rotation = default)
        => EntityManager.PredictedSpawn(entityProtoId, coordinates, overrides: componentOverrides, rotation: rotation);


    /// <inheritdoc cref="PredictedSpawn(string, ComponentRegistry?, bool)"/>
    // public new EntityUid PredictedSpawnAttachedTo(string entityProtoId, EntityCoordinates coordinates, ComponentRegistry? componentOverrides = null, Angle rotation = default)
    //     => FlagPredictedAndReturn(SpawnAttachedTo(entityProtoId, coordinates, overrides: componentOverrides, rotation: rotation));

    // TODO LCDC: FIX THIS SHIT
    public new EntityUid PredictedSpawnAttachedTo(string entityProtoId, EntityCoordinates coordinates, ComponentRegistry? componentOverrides = null, Angle rotation = default)
        => EntityManager.PredictedSpawnAttachedTo(entityProtoId, coordinates, overrides: componentOverrides, rotation: rotation);


    /// <summary>
    ///     Flags the given entity as predicted.
    /// </summary>
    /// <returns>Same <see cref="EntityUid"/> as the one provided, so that some method definitions can be one-lined.</returns>
    protected abstract EntityUid FlagPredictedAndReturn(EntityUid uid);
}
