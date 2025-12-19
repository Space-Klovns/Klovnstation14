using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Client._KS14.DirectionalSpriteOffsetSystem;

/// <summary>
///     This is used, along with a <see cref="SpriteComponent"/>, to
///         make certain layers of a sprite have different pixel offsets
///         when the entity is facing different directions. Absolutely
///         amazing.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DirectionalSpriteOffsetComponent : Component
{
    /// <summary>
    ///     Stains that are on this entity, with their color,
    ///         with the vector's 2 first elements being its X and Y offset,
    ///         and 3rd element being from 0 to 1 specifying its rotation.
    /// </summary>
    [DataField]
    public List<(Vector3, Color)> Stains = new();
}
