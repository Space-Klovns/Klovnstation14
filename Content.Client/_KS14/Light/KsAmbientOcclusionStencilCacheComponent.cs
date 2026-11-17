namespace Content.Client._KS14.Light;

/// <summary>
///     A grid's floor, merged into as few rectangles as possible per block of tiles, for the ambient occlusion's
///         stencil. Added to a grid the first time the AO draws it; a block is rebuilt only when one of its tiles changes.
/// </summary>
[RegisterComponent, Access(typeof(KsAmbientOcclusionStencilSystem))]
public sealed partial class KsAmbientOcclusionStencilCacheComponent : Component
{
    /// <summary>
    ///     Grid-local rectangles covering each block's non-space tiles, keyed by block index (see
    ///         <see cref="KsAmbientOcclusionStencilSystem.BlockSize"/>). A block absent here has not been built yet, or
    ///         was changed since.
    /// </summary>
    [ViewVariables]
    public Dictionary<Vector2i, List<Box2>> Blocks = new();
}
