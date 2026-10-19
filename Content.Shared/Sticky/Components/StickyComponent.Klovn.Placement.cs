using System.Numerics;

namespace Content.Shared.Sticky.Components;

public sealed partial class StickyComponent
{
    /// <summary>
    ///     The position offset applied to the entity after it is stuck.
    ///         Is affected by rotation.
    /// </summary>
    [DataField]
    public Vector2 StuckOffset = Vector2.Zero;
}
