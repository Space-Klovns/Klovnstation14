using Content.Shared._KS14.SalvageTablet;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.SalvageTablet;

/// <summary>
///     A salvager's S.P.A.M. account, kept on their ID card.
/// </summary>
[RegisterComponent]
[Access(typeof(KsSalvageTabletSystem))]
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

    /// <summary>
    ///     Shifted with the card on unpause, together with <see cref="NextDropTime"/>, <see cref="NextOrderTime"/> and each
    ///         task's end time, by <see cref="KsSalvageTabletSystem"/>.
    /// </summary>
    [DataField]
    public List<KsSalvageActiveTask> Tasks = [];

    /// <summary>
    ///     How many tasks the account is offered at once. Set by the tablet it logs in with.
    /// </summary>
    [DataField]
    public int TaskSlots = 6;

    /// <summary>
    ///     The ranks the account climbs. Set by the tablet it logs in with.
    /// </summary>
    [DataField]
    public ProtoId<KsSalvageRankLadderPrototype> RankLadder = "KsSalvageRanks";

    /// <summary>
    ///     When the account may drop a task again.
    /// </summary>
    [DataField]
    public TimeSpan NextDropTime;

    /// <summary>
    ///     When the account may order again.
    /// </summary>
    [DataField]
    public TimeSpan NextOrderTime;

    /// <summary>
    ///     The tablets logged in with the card. Derived from each tablet's <see cref="KsSalvageTabletComponent.BoundCard"/>,
    ///         so not saved.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> Tablets = [];
}
