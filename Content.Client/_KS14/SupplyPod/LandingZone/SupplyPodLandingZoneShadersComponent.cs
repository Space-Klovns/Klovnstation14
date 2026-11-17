using Robust.Client.Graphics;

namespace Content.Client._KS14.SupplyPod.LandingZone;

/// <summary>
///     The pixel-snapping shader instances on a landing zone's two layers. They are the indicator's
///         own, because the slider's pixel count changes every frame, and are disposed with it.
/// </summary>
[RegisterComponent]
public sealed partial class SupplyPodLandingZoneShadersComponent : Component
{
    [ViewVariables]
    public ShaderInstance? ZoneShader;

    [ViewVariables]
    public ShaderInstance? SliderShader;
}
