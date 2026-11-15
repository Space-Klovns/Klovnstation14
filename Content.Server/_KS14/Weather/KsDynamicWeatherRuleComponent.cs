using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Weather;

/// <summary>
///     Weather that changes over the round on its own: a set of states, each some weather (or none) that lasts a while
///         and then gives way to another, picked by weight. Applied to the round's main map, or to one set with
///         <see cref="KsDynamicWeatherRuleSystem.SetMap"/>.
/// </summary>
[RegisterComponent, Access(typeof(KsDynamicWeatherRuleSystem))]
public sealed partial class KsDynamicWeatherRuleComponent : Component
{
    /// <summary>
    ///     Every state the weather can be in. Names must be unique.
    /// </summary>
    [DataField(required: true)]
    public List<KsWeatherState> States = [];

    /// <summary>
    ///     The state the round starts in. Unset, one is picked by weight.
    /// </summary>
    [DataField]
    public string? InitialState;

    /// <summary>
    ///     The map the weather is on, once the rule has found it.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? MapUid;

    /// <summary>
    ///     Index into <see cref="States"/> of the weather now, or -1 before the first.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public int StateIndex = -1;

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextChange;
}

/// <summary>
///     One kind of weather for <see cref="KsDynamicWeatherRuleComponent"/>.
/// </summary>
[DataDefinition]
public sealed partial class KsWeatherState
{
    /// <summary>
    ///     What other states, and admins, call it.
    /// </summary>
    [DataField(required: true)]
    public string Name = string.Empty;

    /// <summary>
    ///     The weather status effects that make it up, together: rain with snow is sleet. None is clear skies.
    /// </summary>
    [DataField]
    public List<EntProtoId> Weathers = [];

    [DataField]
    public TimeSpan MinDuration = TimeSpan.FromMinutes(3);

    [DataField]
    public TimeSpan MaxDuration = TimeSpan.FromMinutes(6);

    /// <summary>
    ///     How likely it is to be picked when nothing narrows the choice: to start the round, or after a state with no
    ///         <see cref="Next"/>.
    /// </summary>
    [DataField]
    public float Weight = 1f;

    /// <summary>
    ///     What it can turn into, by name, with each one's weight. Empty, any other state can follow, by
    ///         <see cref="Weight"/>.
    /// </summary>
    [DataField]
    public Dictionary<string, float> Next = new();
}
