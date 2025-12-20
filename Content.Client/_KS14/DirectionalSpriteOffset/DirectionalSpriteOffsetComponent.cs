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
    ///     Dictionary of layers, and their offsets per <see cref="RsiDirection"/>. 
    /// </summary>
    [DataField]
    public Dictionary<object, Dictionary<RsiDirection, Vector2>> LayerOffsetData = new();
}
