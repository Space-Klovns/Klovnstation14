using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._KS14.SupplyPod;

/// <summary>
///     Swaps a supply pod's sprite draw depth between two values depending on whether the pod is
///     still falling or has already landed, so a pod in transit draws over the things it is flying
///     above and then settles back into the world once it hits the ground.
/// </summary>
/// <remarks>
///     Client-only - draw depth is purely visual, so this never needs to exist server-side.
/// </remarks>
[RegisterComponent]
[Access(typeof(SupplyPodDrawDepthSystem))]
public sealed partial class SupplyPodDrawDepthComponent : Component
{
    /// <summary>
    ///     Draw depth used while the pod is descending.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public DrawDepth TransitDrawDepth = DrawDepth.Overlays;

    /// <summary>
    ///     Draw depth used once the pod has landed. Null keeps whatever depth the sprite was
    ///     authored with, which is the usual case - only the transit depth is special.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public DrawDepth? LandedDrawDepth = null;

    /// <summary>
    ///     The sprite's draw depth before this component touched it, restored on shutdown and used
    ///     as the landed depth when <see cref="LandedDrawDepth"/> is unset.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public int? OriginalDrawDepth = null;
}
