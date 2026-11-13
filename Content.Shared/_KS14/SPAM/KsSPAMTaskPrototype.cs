using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.SPAM;

/// <summary>
///     A task the S.P.A.M. can hand to a salvager.
/// </summary>
[Prototype]
public sealed partial class KsSPAMTaskPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    ///     The entity pictured on the task: the ore to bring or the creature to kill.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Icon;

    /// <summary>
    ///     What has to be done, <see cref="MinAmount"/> to <see cref="MaxAmount"/> times.
    /// </summary>
    [DataField(required: true)]
    public KsSPAMObjective Objective = default!;

    [DataField]
    public int MinAmount = 1;

    [DataField]
    public int MaxAmount = 1;

    /// <summary>
    ///     Mining points paid for each unit of the amount.
    /// </summary>
    [DataField]
    public int PointsPerUnit;

    /// <summary>
    ///     Reputation paid for finishing the task, whatever its amount.
    /// </summary>
    [DataField]
    public int Reputation;

    /// <summary>
    ///     How long the salvager has before the task is replaced.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromMinutes(20);

    /// <summary>
    ///     The lowest rank, counted from zero, that is offered this task.
    /// </summary>
    [DataField]
    public int MinRank;

    /// <summary>
    ///     How likely the task is to be picked, against the other tasks on offer.
    /// </summary>
    [DataField]
    public float Weight = 1f;
}

/// <summary>
///     What a <see cref="KsSPAMTaskPrototype"/> asks for. A new kind of task is a new inheritor, and whichever system
///         sees the thing happen reports it to the S.P.A.M. system.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class KsSPAMObjective
{
}

/// <summary>
///     Put ore of one kind into an ore bag that has the tablet attached.
/// </summary>
public sealed partial class KsSPAMOreObjective : KsSPAMObjective
{
    [DataField(required: true)]
    public ProtoId<StackPrototype> Stack;
}

/// <summary>
///     Scan the dead body of a creature with the tablet.
/// </summary>
public sealed partial class KsSPAMCorpseObjective : KsSPAMObjective
{
    /// <summary>
    ///     The creatures that count.
    /// </summary>
    [DataField(required: true)]
    public List<EntProtoId> Prototypes = [];
}
