using Robust.Shared.GameStates;

namespace Content.Shared._KS14.WaveDistortion;

/// <summary>
///     Added to a map to multiply the speed of any wave distortion on it.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class KsMapWaveDistortionModifierComponent : Component
{
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Multiplier = 1f;
}
