using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Components.Mobs;

/// <summary>
/// Marks a mob as capable of soft-critical: while in <see cref="MobState.Critical"/>
/// but below <see cref="CriticalThreshold"/> damage, the mob stays prone but can
/// whisper and slowly crawl. Added to playable species only.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SoftCriticalComponent : Component
{
    /// <summary>
    /// Damage at which soft-crit ends and hard critical begins (immobile, mute).
    /// Must be greater than the yaml Critical threshold.
    /// </summary>
    [DataField("criticalThreshold")]
    public float CriticalThreshold = 150f;
}
