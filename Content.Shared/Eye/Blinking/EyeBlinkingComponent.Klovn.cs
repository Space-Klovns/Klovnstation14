using Content.Shared.Humanoid;

namespace Content.Shared.Eye.Blinking;

public sealed partial class EyeBlinkingComponent
{
    /// <summary>
    ///     The holder's humanoid layers that cover these eyelids. While any of them is hidden (a mask or helmet
    ///         occluding it), the eyelid layers are kept hidden too, so they don't blink on top of whatever hides the
    ///         eyes.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<HumanoidVisualLayers> KsHidingLayers = new()
    {
        HumanoidVisualLayers.Eyes,
        HumanoidVisualLayers.Head,
    };

    /// <summary>
    ///     Whether these eyes close when their holder dies. When false, as upstream has it, a corpse stares with its
    ///         eyes open, and a blink in progress at the moment of death is finished rather than frozen half-shut.
    ///         Eyes already closed for another reason (sleep, the close-eyes action) stay closed either way.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool KsCloseEyesOnDeath;
}
