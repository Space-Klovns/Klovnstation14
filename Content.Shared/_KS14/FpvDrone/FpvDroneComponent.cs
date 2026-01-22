using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.FpvDrone;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedFpvDroneSystem))]
[AutoGenerateComponentState]
public sealed partial class FpvDroneComponent : Component
{
    /// <summary>
    ///     Drone flying sound specifier. This must loop.
    /// </summary>
    [DataField, ViewVariables]
    public SoundSpecifier? AudioSpecifier = null;

    [DataField, ViewVariables]
    public float FlybySoundProbability = 0.65f;

    /// <summary>
    ///     UID of the audio entity used for the drone flying sound.
    /// </summary>
    [AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? AudioUid;

    /// <summary>
    ///     Is the drone currently flying and using power?
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Active = false;
}

[Serializable, NetSerializable]
public enum FpvDroneVisuals : byte { Active }
