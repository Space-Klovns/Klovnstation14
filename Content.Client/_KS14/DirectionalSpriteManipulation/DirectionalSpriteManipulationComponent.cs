// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using Robust.Shared.Graphics.RSI;

namespace Content.Client._KS14.DirectionalSpriteManipulation;

/// <summary>
///     This is used, along with a <see cref="Robust.Client.GameObjects.SpriteComponent"/>, to
///         make certain layers of a sprite have different pixel offsets/rotation
///         when the entity is facing different directions. Absolutely
/// </summary>
[RegisterComponent]
public sealed partial class DirectionalSpriteManipulationComponent : Component
{
    /// <summary>
    ///     Dictionary of layers by their STRING key. This is otherwise identical to
    ///         <see cref="LayerData"/>
    /// </summary>
    [DataField("data")]
    [Access(typeof(DirectionalSpriteManipulationSystem), Other = AccessPermissions.Read)]
    public Dictionary<string, Dictionary<RsiDirection, DirectionalSpriteManipulationData>>? LayerDataMappings = new();

    /// <summary>
    ///     Dictionary of layers by their mapped key, and their data per <see cref="RsiDirection"/>.
    ///         The layer must exist otherwise an exception will be thrown.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<object, Dictionary<RsiDirection, DirectionalSpriteManipulationData>> LayerData = new();

    /// <summary>
    ///     If not null, then this is the possible RSI directions of the
    ///         sprite in terms of manipulation data.
    /// </summary>
    [DataField]
    [Access(typeof(DirectionalSpriteManipulationSystem), Other = AccessPermissions.Read)]
    public RsiDirectionType? OverrideRsiDirections = null;
}

[DataDefinition]
public partial struct DirectionalSpriteManipulationData
{
    /// <summary>
    ///     Offset to set.
    /// </summary>
    [DataField]
    public Vector2 Offset = Vector2.Zero;

    /// <summary>
    ///     Rotation to set.
    ///         If null, then it is not set.
    /// </summary>
    [DataField]
    public Angle? Rotation = null;

    public DirectionalSpriteManipulationData(Vector2 offset, Angle? rotation = null)
    {
        Offset = offset;
        Rotation = rotation;
    }
}
