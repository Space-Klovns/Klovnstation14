namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     Doctrines: each wave the commander picks one, which changes what its budget goes on, so that one wave is all
///         infantry and the next comes by air, or it holds back to save up for a heavier one.
/// </summary>
public sealed partial class KsFieldCommanderRuleComponent
{
    /// <summary>
    ///     What the commander picks one of, by weight, for each wave, from those whose
    ///         <see cref="KsFieldCommanderDoctrine.MinPhase"/> has been reached. With none, every wave buys by the assets'
    ///         own weights.
    /// </summary>
    [DataField]
    public List<KsFieldCommanderDoctrine> Doctrines = [];

    /// <summary>
    ///     Index into <see cref="Doctrines"/> of the last wave's doctrine, or -1.
    /// </summary>
    [ViewVariables]
    public int DoctrineIndex = -1;
}

public sealed partial class KsFieldCommanderAsset
{
    /// <summary>
    ///     Free-form labels a <see cref="KsFieldCommanderDoctrine"/> weighs this by: <c>infantry</c>, <c>elite</c>,
    ///         <c>pod</c>, and so on.
    /// </summary>
    [DataField]
    public HashSet<string> Tags = new();
}

/// <summary>
///     How the commander spends one wave's budget.
/// </summary>
[DataDefinition]
public sealed partial class KsFieldCommanderDoctrine
{
    /// <summary>
    ///     What admins see it called.
    /// </summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    ///     How likely it is to be picked against the others.
    /// </summary>
    [DataField]
    public float Weight = 1f;

    /// <summary>
    ///     Index of the first phase it can be picked in.
    /// </summary>
    [DataField]
    public int MinPhase;

    /// <summary>
    ///     Multiplied into the weight of every asset with that tag, once per tag it has: above 1 to favour it, below to
    ///         shun it, 0 to rule it out. Assets with none of these tags keep their own weight.
    /// </summary>
    [DataField]
    public Dictionary<string, float> TagWeights = new();

    /// <summary>
    ///     How much of the wave's budget is spent. Below 1, the rest is carried over to the next wave (up to one wave's
    ///         worth, as with any leftover), for the commander to save up.
    /// </summary>
    [DataField]
    public float BudgetMultiplier = 1f;

    /// <summary>
    ///     The commander's taunt with a wave sent under this doctrine.
    /// </summary>
    [DataField]
    public LocId? Announcement;

    /// <summary>
    ///     The defenders' intelligence on a wave sent under this doctrine.
    /// </summary>
    [DataField]
    public LocId? Intel;
}
