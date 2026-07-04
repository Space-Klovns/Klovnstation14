using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Scenario.Components;

/// <summary>
///     Specifies a scenario faction. This only exists as a prototype so that yaml can easily be validated.
/// </summary>
[Prototype]
public sealed partial class ScenarioFactionPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     Loc text to be appended on round end if this
    ///         faction wins.
    /// </summary>
    [DataField(required: true)]
    public LocId VictoryLocId;

    /// <summary>
    ///     Loc text to be appended on round end if this
    ///         faction wins, for each respective win type.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<ScenarioWinType, LocId> WinTypeLocIds = [];
}

public enum ScenarioWinType : byte
{
    Decimation,

    /// <summary>
    ///     The objective was captured or something.
    /// </summary>
    Objective
}
