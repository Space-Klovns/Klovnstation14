using Content.Shared._KS14.OreWell;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.OreVent;

/// <summary>
///     Should have <see cref="Buckle.Components.StrapComponent"/>, for the drone.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(fieldDeltas: true)]
[AutoGenerateComponentPause]
public sealed partial class OreVentComponent : Component
{
    /// <summary>
    ///     Whether an ore well is on this vent, and it will
    ///         produce boulders.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public bool Tapped = false;

    /// <summary>
    ///     Is the area around the vent being cleared?
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public bool DoingClearing = false;

    /// <summary>
    ///     Is this vent in the process of being tapped?
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool BeingTapped = false;

    /// <summary>
    ///     How many times the area surrounding this will be cleared.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public int ClearingIterations = 3;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public int ClearRadius = 6;

    /// <summary>
    ///     Entity spawned during the process of tapping, and deleted
    ///         when no longer tapping.
    /// </summary>
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public EntProtoId TappingProcessEntityProto;

    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? TappingProcessEntityUid = null;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public EntProtoId? DroneEntityProto = null;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public EntityUid? DroneEntityUid = null;

    /// <summary>
    ///     Duration of do-after to start extraction.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan PreExtractionDuration = TimeSpan.Zero;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan ClearingDuration = TimeSpan.Zero;

    /// <summary>
    ///     How long it takes to finish extraction.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan ExtractionDuration = TimeSpan.Zero;

    /// <summary>
    ///     Game-time when tapping will be finished.
    ///         If this vent is not being tapped, then
    ///         this value is undefined.
    /// </summary>
    [DataField, AutoNetworkedField]
    [AutoPausedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan TappingFinishedTime = TimeSpan.MinValue;

    /// <summary>
    ///     Ore well setting to be applied when this gets tapped.
    /// </summary>
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<OreWellSettingPrototype> OreWellSettingId;
}

/// <summary>
///     Added to orevents that are clearing.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class ActiveClearingOreVentComponent : Component
{
    [AutoNetworkedField, DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan IterationDelay = TimeSpan.Zero;

    [DataField]
    [AutoNetworkedField, AutoPausedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextIteration = TimeSpan.MinValue;

    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public int Iteration = 0;
}

[Serializable, NetSerializable]
public enum OreVentVisuals
{
    /// <summary>
    ///     Boolean
    /// </summary>
    Tapped
}

[Serializable, NetSerializable]
public sealed partial class OreVentPreExtractionDoAfterEvent : DoAfterEvent
{
    public override DoAfterEvent Clone() => this;
}

/// <summary>
///     Doafter for the tapping process.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class OreVentTappingDoAfterEvent : DoAfterEvent
{
    public override DoAfterEvent Clone() => this;
}
