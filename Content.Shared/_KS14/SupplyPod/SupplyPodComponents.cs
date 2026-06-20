using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.SupplyPod;

[Access(typeof(SharedSupplyPodSystem))]
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class SupplyPodComponent : Component
{
    /// <summary>
    ///     Is it open, if theres a door?
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    [AutoNetworkedField]
    public bool Open = false;

    [DataField(serverOnly: true), ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan TransitDuration = TimeSpan.Zero;

    /// <summary>
    ///     Height that this starts descent from/ascends to.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Height = 5f;

    /// <summary>
    ///     Maximum random angle to fall at.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public Angle AngularDeviation;

    #region Fall data
    /// <summary>
    ///     Amount of time taken to fall down to the ground.
    /// </summary>
    [DataField]
    public TimeSpan FallDuration = TimeSpan.Zero;

    /// <summary>
    ///     Time from fall start that
    ///         the fall sound will be played.
    /// </summary>
    [DataField]
    public TimeSpan FallSoundDelay = TimeSpan.Zero;

    /// <summary>
    ///     Sound to be played at the LZ while the
    ///         pod is falling, if any.
    /// </summary>
    [DataField]
    public SoundSpecifier? FallSound = null;
    #endregion

    #region Impact data
    [DataField]
    public SoundSpecifier? ImpactSound = null;
    #endregion
}

/// <summary>
///     Added to supply pods when they are in transit, removed afterwards.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class ActiveSupplyPodComponent : Component
{
    /// <summary>
    ///     Where the pod is planning to impact.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    [AutoNetworkedField]
    public EntityCoordinates DestinationCoordinates;

    /// <summary>
    ///     Game-time of when the pod will finish descent/finish ascent.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan LaunchFinishTime;

    /// <summary>
    ///     Game-time of when the pod fall sound should be played.
    ///         Set to <see cref="TimeSpan.MaxValue"/>  after the sound is played.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan FallSoundTime = TimeSpan.Zero;

    /// <summary>
    ///     Angle that the pod comes in at. This is a counter-clockwise
    ///         offset from 'completely straight down' (0deg).
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    [AutoNetworkedField]
    public Angle Angle;
}

[Access(typeof(SharedSupplyPodSystem))]
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class SupplyPodDoorDrawerComponent : Component
{
    [DataField, AutoNetworkedField]
    public Angle Rotation = Angle.Zero;

    /// <summary>
    ///     Must point to an RSI, not raw texture.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public PrototypeLayerData DoorData;

    /// <summary>
    ///     Must point to an RSI, not raw texture.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public PrototypeLayerData DecalData;
}

[Serializable, NetSerializable]
public enum SupplyPodVisuals : byte
{
    /// <summary>
    ///     Boolean
    /// </summary>
    Landed
}
