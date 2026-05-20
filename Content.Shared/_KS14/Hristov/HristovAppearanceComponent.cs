using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Hristov;

/// <summary>
/// Displays a sprite above an entity.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class HristovAppearanceComponent : Component
{
    /// <summary>
    /// The sprite to display above the entity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SpriteSpecifier? Sprite = new SpriteSpecifier.Rsi(new ResPath("_KS14/Objects/Misc/hristov_hole.rsi"), "base");

    /// <summary>
    /// The scale of the sprite.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2 Scale = Vector2.One;
}
