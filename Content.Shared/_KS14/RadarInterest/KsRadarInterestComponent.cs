using Robust.Shared.Serialization;

namespace Content.Shared._KS14.RadarInterest;

/// <summary>
///     For something that should show on mass scanner.
///         They're called "interest"s.
/// </summary>
[RegisterComponent]
[Access(typeof(KsRadarInterestSystem))]
public sealed partial class KsRadarInterestComponent : Component
{
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadOnly)]
    public KsRadarInterestData Data;
}

[DataDefinition]
[NetSerializable, Serializable]
public sealed partial class KsRadarInterestData
{
    /// <summary>
    ///     Text to display. If empty, then this will be the
    ///         name of the thing.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public string Text = "radar interest";

    /// <summary>
    ///     If false, the interest is drawn as a bare dot, with no <see cref="Text"/> and no name in its place.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public bool ShowText = true;
}
