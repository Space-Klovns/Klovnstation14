using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Emissive;

/// <summary>
///     Used for marking (a) layer(s) as emissive; i.e., it will also be drawn with light blur.
///         The layer MUST exist.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EmissiveLayersComponent : Component
{
    /// <summary>
    ///     Keys of layers affected.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public List<string> Layers = [];

    /// <summary>
    ///     Enlargement to glow.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float GlowRadius = 0.25f;


    /// <summary>
    ///     Enlargement to glow.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float ROTOF = 0f;

    /// <summary>
    ///     Actual intensity of the shader.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Intensity = 2.2f;

    /// <summary>
    ///     Offset to center of the glow effect.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public Vector2 Offset = Vector2.Zero;

    /// <summary>
    ///     If true, then the sprites rotation/offset and layers rotation/offsets
    ///         will be applied when rendering emissive layers.
    ///
    ///     Eye rotation and <see cref="Offset"/> are both still applied no matter what.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool UseSpriteTransform = true;
}
