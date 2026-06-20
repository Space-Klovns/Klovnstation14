using System.Numerics;
using Robust.Shared.Utility;

namespace Content.Client._KS14.ShadowOverlay;

/// <summary>
///     Renders a shadow under the mob.
///         Offset applied to the shadow isn't affected by rotation, unlike
///         normal sprite layers.
/// </summary>
[RegisterComponent]
public sealed partial class KsShadowComponent : Component
{
    [DataField(required: true)]
    public Enum Visuals;

    [DataField("states")]
    public Dictionary<string, PrototypeLayerData> SpritesPerKey = [];

    // Vars
    /// <summary>
    ///     Null if no sprite is present.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public SpriteSpecifier? Sprite = null;

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public Angle Rotation = Angle.Zero;

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public Vector2 Offset = Vector2.Zero;

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public Color Modulate = Color.White;
}
