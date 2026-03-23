// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using Robust.Shared.Graphics.RSI;

namespace Content.Client._KS14.DirectionalSpriteOffset;

/// <summary>
///     This is used, along with a <see cref="Robust.Client.GameObjects.SpriteComponent"/>, to
///         make certain layers of a sprite have different pixel offsets
///         when the entity is facing different directions. Absolutely
///         amazing.
/// </summary>
[RegisterComponent]
public sealed partial class DirectionalSpriteOffsetComponent : Component
{
    /// <summary>
    ///     Dictionary of layers by their STRING key. This is otherwise identical to
    ///         <see cref="LayerOffsetData"/>
    /// </summary>
    [DataField("offsetData")]
    [Access(typeof(DirectionalSpriteOffsetSystem), Other = AccessPermissions.Read)]
    public Dictionary<string, Dictionary<RsiDirection, Vector2>>? LayerOffsetDataMappings = new();

    /// <summary>
    ///     Dictionary of layers by their mapped key, and their offsets per <see cref="RsiDirection"/>.
    ///         The layer must exist otherwise an exception will be thrown.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<object, Dictionary<RsiDirection, Vector2>> LayerOffsetData = new();
}
