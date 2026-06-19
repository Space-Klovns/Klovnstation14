using Content.Shared._KS14.Atmos.EntitySystems;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Atmos.Components;

[RegisterComponent, NetworkedComponent]
[Access(typeof(KsGasMaxPressureIntervalSystem))]
public sealed partial class KsGasMaxPressureSoundComponent : Component
{
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier OverpressureSound;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier? FinalOverpressureSound = null;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float FinalOverpressureThreshold = 0.5f;
}
