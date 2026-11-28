using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Stacks;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.WallHealer;

/// <summary>
///     A wall-mounted first aid station. A patient puts their hand in it, and while it stays there the station treats
///         them from its <see cref="Pools"/> every <see cref="TreatmentInterval"/>. The pools recharge while nobody is
///         using it. It also hands out supplies (gauze) that can be restocked.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(raiseAfterAutoHandleState: true), AutoGenerateComponentPause]
[Access(typeof(KsWallHealerSystem))]
public sealed partial class KsWallHealerComponent : Component
{
    #region Pools

    /// <summary>
    ///     What this station treats, and how much of each it holds.
    /// </summary>
    [DataField(required: true)]
    public List<KsWallHealerPool> Pools = new();

    /// <summary>
    ///     How much each of <see cref="Pools"/> holds now, by index. Filled on map init.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<float> PoolAmounts = new();

    /// <summary>
    ///     How full the pools start, as a fraction of their capacity.
    /// </summary>
    [DataField]
    public float InitialFill = 1f;

    /// <summary>
    ///     The most a pool gives in one treatment, as a fraction of its capacity.
    /// </summary>
    [DataField]
    public float TreatmentFraction = 0.1f;

    /// <summary>
    ///     How much a pool refills each recharge, as a fraction of its capacity.
    /// </summary>
    [DataField]
    public float RechargeFraction = 0.1f;

    [DataField]
    public TimeSpan RechargeInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     When the pools next recharge, if nobody is using the station and it is powered.
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan NextRecharge;

    #endregion

    #region Patient

    [DataField]
    public TimeSpan TreatmentInterval = TimeSpan.FromSeconds(4);

    /// <summary>
    ///     How long it takes a patient to put their own hand in.
    /// </summary>
    [DataField]
    public TimeSpan SelfInsertDelay = TimeSpan.FromSeconds(0.5);

    /// <summary>
    ///     How long it takes to put someone else's hand in.
    /// </summary>
    [DataField]
    public TimeSpan OtherInsertDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     The earliest the next treatment can happen. Only reset for a new patient, so taking your hand out and
    ///         putting it back in doesn't skip the wait.
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan NextTreatment;

    /// <summary>
    ///     Whose hand is in the station now.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? Patient;

    /// <summary>
    ///     The index of the patient's treatment do-after, which runs for as long as their hand is in.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public ushort? TreatmentDoAfter;

    /// <summary>
    ///     Whose hand was in the station last, while nobody's is.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? LastPatient;

    /// <summary>
    ///     Treatments attempted on the current patient, so feedback is only shown every so often.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public int TreatmentCount;

    /// <summary>
    ///     A failed treatment tells the patient why once every this many tries.
    /// </summary>
    [DataField]
    public int FailureFeedbackInterval = 3;

    /// <summary>
    ///     A successful treatment says so once every this many treatments.
    /// </summary>
    [DataField]
    public int SuccessFeedbackInterval = 2;

    /// <summary>
    ///     The damage containers of patients the station can treat. Anyone else is refused.
    /// </summary>
    [DataField]
    public List<ProtoId<DamageContainerPrototype>> TreatableDamageContainers = new() { "Biological" };

    /// <summary>
    ///     What the station does to its patient each treatment once emagged, instead of treating them.
    /// </summary>
    [DataField]
    public DamageSpecifier EmaggedDamage = new() { DamageDict = new() { { "Piercing", 33 } } };

    #endregion

    #region ID cards

    /// <summary>
    ///     Whether a patient needs an ID card on them to be treated.
    /// </summary>
    [DataField]
    public bool RequiresIdCard;

    /// <summary>
    ///     Whether treatment is taken from the patient's ID card's <see cref="KsWallHealerAllowanceComponent"/>, shared by
    ///         every station that uses it. A patient without an ID card is then not limited, unless
    ///         <see cref="RequiresIdCard"/> is set.
    /// </summary>
    [DataField]
    public bool UsesIdAllowance = true;

    #endregion

    #region Supplies

    /// <summary>
    ///     The container holding the supplies the station hands out.
    /// </summary>
    [DataField]
    public string SupplyContainerId = "supplies";

    /// <summary>
    ///     The stack the station is restocked with and hands out, one at a time.
    /// </summary>
    [DataField]
    public ProtoId<StackPrototype> SupplyStackType = "Gauze";

    /// <summary>
    ///     The chance that an emagged station jams instead of handing out a supply.
    /// </summary>
    [DataField]
    public float EmaggedSupplyJamChance = 0.99f;

    #endregion

    #region Visuals

    /// <summary>
    ///     The state prefix of a pool's fill layer. The prefix followed by 0 is a full pool, followed by
    ///         <see cref="FillSteps"/> an empty one.
    /// </summary>
    [DataField]
    public string FillStatePrefix = "bar";

    [DataField]
    public int FillSteps = 7;

    #endregion

    #region Sounds

    [DataField]
    public SoundSpecifier TreatedSound = new SoundPathSpecifier("/Audio/Items/Defib/defib_safety_on.ogg");

    [DataField]
    public SoundSpecifier FailedSound = new SoundPathSpecifier("/Audio/Items/Defib/defib_safety_off.ogg");

    [DataField]
    public SoundSpecifier EmaggedSound = new SoundPathSpecifier("/Audio/Items/Defib/defib_failed.ogg");

    [DataField]
    public SoundSpecifier RechargedSound = new SoundPathSpecifier("/Audio/Items/Defib/defib_ready.ogg");

    [DataField]
    public SoundSpecifier SupplySound = new SoundPathSpecifier("/Audio/Machines/machine_vend.ogg");

    #endregion
}
