using Content.Shared._KS14.SPAM;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.SPAM;

/// <summary>
///     A salvager's S.P.A.M. account, kept on their ID card.
/// </summary>
[RegisterComponent]
[Access(typeof(KsSPAMSystem))]
public sealed partial class KsMinerAccountComponent : Component
{
    /// <summary>
    ///     Mining points: paid by tasks and smelted ore, spent on orders.
    /// </summary>
    [DataField]
    public int Points;

    /// <summary>
    ///     Paid by tasks alone, and never spent. Decides the rank.
    /// </summary>
    [DataField]
    public int Reputation;

    [DataField]
    public List<KsSPAMActiveTask> Tasks = [];

    /// <summary>
    ///     How many tasks the account is offered at once. Set by the tablet it logs in with.
    /// </summary>
    [DataField]
    public int TaskSlots = 6;

    /// <summary>
    ///     The ranks the account climbs. Set by the tablet it logs in with.
    /// </summary>
    [DataField]
    public ProtoId<KsSPAMRankLadderPrototype> RankLadder = "KsSPAMSalvage";

    [DataField]
    public TimeSpan NextDropTime;

    [DataField]
    public TimeSpan NextOrderTime;

    /// <summary>
    ///     Whoever last logged in with the card, for their picture on the tablet.
    /// </summary>
    [DataField]
    public EntityUid? Salvager;
}
