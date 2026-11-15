using Content.Shared.Humanoid;

namespace Content.Shared.Eye.Blinking;

public sealed partial class EyeBlinkingComponent
{
    /// <summary>
    ///     The holder's humanoid layers that cover these eyelids. While any of them is hidden (a mask or helmet
    ///         occluding it), the eyelid layers are kept hidden too, so they don't blink on top of whatever hides the
    ///         eyes.
    /// </summary>
    [DataField]
    public List<HumanoidVisualLayers> KsHidingLayers = new()
    {
        HumanoidVisualLayers.Eyes,
        HumanoidVisualLayers.Eyelids,
        HumanoidVisualLayers.Head,
    };
}
