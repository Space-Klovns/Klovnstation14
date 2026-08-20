using System.Numerics;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.OverlayStains;

/// <summary>
///     Data describing a single stain rendered on an entity with a <see cref="StainedComponent"/>.
///         <see cref="Texture"/> may be of any size; it is always drawn centered on <see cref="Offset"/>.
/// </summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class StainData
{
    /// <summary>
    ///     Texture drawn for this stain.
    /// </summary>
    [DataField(required: true)]
    public SpriteSpecifier Texture = default!;

    /// <summary>
    ///     Offset from the center of the entity this stain is on.
    /// </summary>
    [DataField]
    public Vector2 Offset;

    /// <summary>
    ///     Rotation of the stain, from 0 to 1 representing 0 to a full turn.
    /// </summary>
    [DataField]
    public float Rotation;

    [DataField]
    public Color Color = Color.White;
}
