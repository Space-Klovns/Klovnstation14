using Content.Server._KS14.NPC.Systems;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     The light level at an NPC target, as last computed by <see cref="NpcLightDetectionSystem"/>. Added to a
///         target the first time an NPC asks how lit it is, and reused by every NPC asking again within the same
///         tick.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcLightDetectionSystem))]
public sealed partial class NpcLightLevelCacheComponent : Component
{
    /// <summary>
    ///     From 0 (dark) to 1.
    /// </summary>
    [ViewVariables]
    public float Level;

    /// <summary>
    ///     The tick <see cref="Level"/> was computed on. It is only valid during that tick.
    /// </summary>
    [ViewVariables]
    public GameTick ComputedTick;
}
