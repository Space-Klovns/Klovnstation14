using System.Numerics;
using Robust.Shared.ComponentTrees;
using Robust.Shared.GameStates;
using Robust.Shared.Physics;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Emissive;

/// <summary>
///     Used for marking (a) layer(s) as emissive; i.e., it will also be drawn with light blur.
///         The layer MUST exist.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EmissiveLayersComponent : Component, IComponentTreeEntry<EmissiveLayersComponent>
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

    /// <summary>
    ///     Whether the texture lookup should use rotation only and the actual rendering doesnt.
    ///
    ///     Good for things that have sprites rotated for each direction, like light tubes etc..
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool OnlyRotateTexture = false;

    /// <summary>
    ///     Overrides for individual layers in <see cref="Layers"/>, by the same key. Layers without one use the
    ///         component's own values.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<string, EmissiveLayerParams> LayerParams = new();

    /// <summary>
    ///     Whether the entity is in a container, and so out of the client's tree of emissives. Kept by the client's
    ///         emissive tree system; unused on the server.
    /// </summary>
    [ViewVariables]
    public bool InContainer;

    public EntityUid? TreeUid { get; set; }

    public DynamicTree<ComponentTreeEntry<EmissiveLayersComponent>>? Tree { get; set; }

    public bool AddToTree => !InContainer;

    public bool TreeUpdateQueued { get; set; }
}

/// <summary>
///     How one layer of <see cref="EmissiveLayersComponent"/> glows, where it should differ from the rest.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class EmissiveLayerParams
{
    /// <summary>
    ///     This layer's <see cref="EmissiveLayersComponent.GlowRadius"/>, if it has its own.
    /// </summary>
    [DataField]
    public float? GlowRadius;

    /// <summary>
    ///     Multiplies the colour this layer emits: below 1 to make it glow and light its surroundings less.
    ///         Unlike <see cref="EmissiveLayersComponent.Intensity"/>, which is per entity.
    /// </summary>
    [DataField]
    public float Brightness = 1f;
}
