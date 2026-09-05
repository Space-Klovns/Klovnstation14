using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.SupplyPod.Glow;

/// <summary>
///     Hangs a burning engine glow off a supply pod for the duration of its descent, then lets it
///         fade out where the pod landed.
/// </summary>
/// <remarks>
///     tgstation's <c>add_glow()</c> / <c>end_glow()</c>. Pods with no glow simply don't carry
///         this component, which is that codebase's <c>glow_color = null</c>.
/// </remarks>
[RegisterComponent]
public sealed partial class SupplyPodGlowComponent : Component
{
    /// <summary>
    ///     Glow effect to hang off the pod. Colour variants are separate prototypes, matching
    ///         tgstation's per-style <c>pod_glow_&lt;colour&gt;</c> icon states.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId GlowProto;

    /// <summary>
    ///     How long the glow takes to fade out after landing.
    /// </summary>
    /// <remarks>
    ///     tgstation caps this at 2.5s regardless of how long the pod takes to open.
    /// </remarks>
    [DataField]
    public TimeSpan FadeDuration = TimeSpan.FromSeconds(2.5);

    /// <summary>
    ///     Extra time the glow sticks around after finishing its fade, before deletion.
    /// </summary>
    [DataField]
    public TimeSpan LingerDuration = TimeSpan.FromSeconds(0.5);

    /// <summary>
    ///     The spawned glow. Server-side bookkeeping only.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? GlowEntity;
}
