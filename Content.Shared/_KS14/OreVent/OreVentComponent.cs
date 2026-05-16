using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.OreVent;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(fieldDeltas: true)]
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
    ///     Is pre-extraction doafter happening?
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public bool DoingPreExtraction = false;

    /// <summary>
    ///     Is this vent in the process of being tapped?
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public bool BeingTapped = false;

    /// <summary>
    ///     Duration of do-after to start extraction.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan PreExtractionDuration = TimeSpan.Zero;
}

[Serializable, NetSerializable]
public enum OreVentVisuals
{
    State
}

[Serializable, NetSerializable]
public sealed partial class OreVentPreExtractionEvent : DoAfterEvent
{
    public override DoAfterEvent Clone() => this;
}
