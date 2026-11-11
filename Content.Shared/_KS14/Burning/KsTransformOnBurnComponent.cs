using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Burning;

/// <summary>
///     Turns into another entity the moment it meets fire or lava, instead of burning. tgstation's
///         <c>fire_act</c> override on the unfired giantslayer spear.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsTransformOnBurnComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Prototype;

    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Magic/staff_change.ogg", AudioParams.Default.WithVolume(-6f));
}
