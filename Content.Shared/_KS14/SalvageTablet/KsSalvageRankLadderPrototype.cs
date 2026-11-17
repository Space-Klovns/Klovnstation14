using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.SalvageTablet;

/// <summary>
///     The ranks a salvager climbs with reputation, lowest first.
/// </summary>
[Prototype]
public sealed partial class KsSalvageRankLadderPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<KsSalvageRank> Ranks = [];

    /// <summary>
    ///     The rank, counted from zero, that this much reputation earns.
    /// </summary>
    public int GetRank(int reputation)
    {
        var rank = 0;
        for (var index = 0; index < Ranks.Count; index++)
        {
            if (reputation >= Ranks[index].Reputation)
                rank = index;
        }

        return rank;
    }
}

[DataDefinition]
public sealed partial class KsSalvageRank
{
    [DataField(required: true)]
    public LocId Title;

    /// <summary>
    ///     The reputation at which the rank is reached.
    /// </summary>
    [DataField]
    public int Reputation;
}
