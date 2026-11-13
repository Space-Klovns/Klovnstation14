using Content.Shared.Access;
using Content.Shared.Materials;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.SPAM;

/// <summary>
///     The S.P.A.M. tablet: a salvager logs in with their ID, and from then on it shows that ID's tasks, rank and
///         mining points. Everything personal is kept on the ID card, so a lost tablet loses nothing.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class KsSPAMComponent : Component
{
    /// <summary>
    ///     The ID card this tablet is logged in with.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    [AutoNetworkedField]
    public EntityUid? BoundCard;

    /// <summary>
    ///     The access an ID needs to log in.
    /// </summary>
    [DataField]
    public ProtoId<AccessLevelPrototype> RequiredAccess = "Salvage";

    [DataField]
    [AutoNetworkedField]
    public ProtoId<KsSPAMRankLadderPrototype> RankLadder = "KsSPAMSalvage";

    /// <summary>
    ///     How many tasks an account is offered at once.
    /// </summary>
    [DataField]
    public int TaskSlots = 6;

    /// <summary>
    ///     How many of them it can track at once.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public int MaxTrackedTasks = 2;

    /// <summary>
    ///     How long an account waits after dropping a task before it can drop another.
    /// </summary>
    [DataField]
    public TimeSpan DropCooldown = TimeSpan.FromMinutes(4);

    /// <summary>
    ///     How long an account waits between orders.
    /// </summary>
    [DataField]
    public TimeSpan OrderCooldown = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     The most of one product an order can hold.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public int MaxOrderAmount = 10;

    /// <summary>
    ///     The pod an order arrives in.
    /// </summary>
    [DataField]
    public EntProtoId PodPrototype = "SupplyPodCargoBase";

    /// <summary>
    ///     How far from the buyer the pod comes down, in tiles.
    /// </summary>
    [DataField]
    public float PodDistance = 1.5f;

    /// <summary>
    ///     How long scanning a body takes.
    /// </summary>
    [DataField]
    public TimeSpan ScanDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Materials the silo list always shows, even when the silo has never held any.
    /// </summary>
    [DataField]
    public List<ProtoId<MaterialPrototype>> ShownMaterials = [];

    /// <summary>
    ///     A material the silo holds this much of or less is marked as running low. In hundredths of a sheet.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public int LowMaterialAmount = 500;

    /// <summary>
    ///     Played to whoever presses Log in.
    /// </summary>
    [DataField]
    public SoundSpecifier? LoginSound = new SoundPathSpecifier("/Audio/_KS14/Machines/Terminal/terminal_processing.ogg");

    /// <summary>
    ///     Played to them once the account is on the screen.
    /// </summary>
    [DataField]
    public SoundSpecifier? LoggedInSound = new SoundPathSpecifier("/Audio/_KS14/Machines/Computer/computer_start.ogg", AudioParams.Default.WithVolume(-10f));

    /// <summary>
    ///     Played to whoever opens the details of a task.
    /// </summary>
    [DataField]
    public SoundSpecifier? TaskInfoSound = new SoundPathSpecifier("/Audio/_KS14/Machines/Terminal/terminal_prompt.ogg");

    /// <summary>
    ///     Played to whoever starts tracking a task.
    /// </summary>
    [DataField]
    public SoundSpecifier? TrackSound = new SoundPathSpecifier("/Audio/_KS14/Machines/Terminal/terminal_prompt_confirm.ogg");

    /// <summary>
    ///     Played to the salvager when one of their tasks is finished.
    /// </summary>
    [DataField]
    public SoundSpecifier? CompleteSound = new SoundPathSpecifier("/Audio/_KS14/Machines/Terminal/terminal_success.ogg", AudioParams.Default.WithVolume(-5f));

    /// <summary>
    ///     Played to the salvager when one of their tasks runs out of time.
    /// </summary>
    [DataField]
    public SoundSpecifier? FailSound = new SoundPathSpecifier("/Audio/_KS14/Machines/Terminal/terminal_error.ogg");
}
