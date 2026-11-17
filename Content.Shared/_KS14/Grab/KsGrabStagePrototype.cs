using Content.Shared.Alert;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Grab;

/// <summary>
///     One level of a grab, tgstation's <c>grab_state</c>: passive, aggressive, neck, kill. A grabber climbs the list
///         of stages in its <see cref="KsGrabberComponent"/> by ctrl-clicking whoever they pull. Every effect of a stage
///         is a field here, so a new level, or a stronger grip for some species, is only YAML.
/// </summary>
[Prototype]
public sealed partial class KsGrabStagePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     How long the grabber has to hold still to reach this stage. Zero is instant.
    /// </summary>
    [DataField]
    public TimeSpan UpgradeTime = TimeSpan.Zero;

    /// <summary>
    ///     Whether holding the victim at this stage takes one of the grabber's hands.
    /// </summary>
    [DataField]
    public bool TakesHand;

    /// <summary>
    ///     Whether the victim can't walk away and has to resist to get free.
    /// </summary>
    [DataField]
    public bool BlocksVictimMovement;

    /// <summary>
    ///     Whether the victim is pinned to the floor and can't get up while held.
    /// </summary>
    [DataField]
    public bool FloorsVictim;

    /// <summary>
    ///     Chance that a resist attempt breaks free. Only used when the stage blocks movement: otherwise the victim
    ///         just walks off.
    /// </summary>
    [DataField]
    public float EscapeChance = 1f;

    /// <summary>
    ///     Stamina damage the victim takes from a failed resist, rolled between the two.
    /// </summary>
    [DataField]
    public float ResistFailStaminaDamageMin;

    [DataField]
    public float ResistFailStaminaDamageMax;

    /// <summary>
    ///     How close the grip reels the victim in, in metres between their centres. Null leaves the pull's slack.
    /// </summary>
    [DataField]
    public float? HoldDistance;

    /// <summary>
    ///     How close the victim has to be to reach this stage, and for its <see cref="Damage"/> to be dealt.
    ///     Null has no limit.
    /// </summary>
    [DataField]
    public float? RequiredDistance;

    /// <summary>
    ///     Speed multiplier of the grabber while holding someone at this stage.
    /// </summary>
    [DataField]
    public float GrabberSpeedModifier = 1f;

    /// <summary>
    ///     Extra speed multiplier of the grabber while the victim lies on the floor.
    /// </summary>
    [DataField]
    public float ProneDragSpeedModifier = 1f;

    /// <summary>
    ///     Damage dealt to the victim every <see cref="DamageInterval"/> while held at this stage.
    /// </summary>
    [DataField]
    public DamageSpecifier? Damage;

    [DataField]
    public TimeSpan DamageInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Popup shown to the victim now and then while they take <see cref="Damage"/>.
    /// </summary>
    [DataField]
    public LocId? DamagePopup;

    /// <summary>
    ///     Whether throwing the grab's hand throws the victim. Needs <see cref="TakesHand"/>.
    /// </summary>
    [DataField]
    public bool AllowsThrow;

    [DataField]
    public float ThrowSpeedModifier = 1f;

    /// <summary>
    ///     Alert shown to the victim instead of the usual pulled alert. Clicking it resists.
    /// </summary>
    [DataField]
    public ProtoId<AlertPrototype>? VictimAlert;

    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Effects/thudswoosh.ogg");

    /// <summary>
    ///     Popups when the grabber starts tightening their grip towards this stage. Only used with an upgrade time.
    ///         Each gets <c>grabber</c> and <c>victim</c>.
    /// </summary>
    [DataField]
    public LocId StartOthersPopup = "ks-grab-start-others";

    [DataField]
    public LocId StartUserPopup = "ks-grab-start-user";

    [DataField]
    public LocId StartTargetPopup = "ks-grab-start-target";

    /// <summary>
    ///     Popups when the grab reaches this stage. Each gets <c>grabber</c> and <c>victim</c>. Null shows nothing to
    ///         that audience, as for a plain pull, whose victim already gets the pulling system's popup.
    /// </summary>
    [DataField]
    public LocId? EnterOthersPopup;

    [DataField]
    public LocId? EnterUserPopup;

    [DataField]
    public LocId? EnterTargetPopup;
}
