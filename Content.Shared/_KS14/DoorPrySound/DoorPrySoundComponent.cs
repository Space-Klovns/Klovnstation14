using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.DoorPrySound;

[RegisterComponent, NetworkedComponent]
[Access(typeof(DoorPrySoundSystem))]
public sealed partial class DoorPrySoundComponent : Component
{
    [DataField]
    public SoundSpecifier? OpenSound = null;

    [DataField]
    public SoundSpecifier? CloseSound = null;
}
