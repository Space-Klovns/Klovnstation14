using Content.Shared._KS14.KsPopup;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._KS14.Grab;

/// <summary>
///     Lets a puller tighten their grip on a <see cref="KsGrabbableComponent"/> they pull, one stage at a time, by
///         ctrl-clicking them again. Pulling is the first stage; letting go of the pull resets it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(KsGrabSystem))]
public sealed partial class KsGrabberComponent : Component
{
    /// <summary>
    ///     The grab ladder, weakest first. The first stage is a plain pull.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<ProtoId<KsGrabStagePrototype>> Stages = new()
    {
        "KsGrabPassive",
        "KsGrabAggressive",
        "KsGrabNeck",
        "KsGrabStrangle",
    };

    /// <summary>
    ///     Index into <see cref="Stages"/> of the grip on whoever is pulled.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Stage;

    /// <summary>
    ///     Whether a shove has pinned the victim to the floor. They stay down until the grab ends.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Pinned;

    /// <summary>
    ///     The virtual item that holds the victim in a hand, if the stage takes one.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? HandItem;

    /// <summary>
    ///     How long a stage that floors its victim, or a pin, knocks them down for. Standing up is blocked for as long
    ///         as they are held, so this only has to cover the fall.
    /// </summary>
    [DataField]
    public TimeSpan FloorKnockdownTime = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     How fast a tightening grip draws the victim in to its stage's hold distance, in metres per second. Quick,
    ///         since the change of distance interrupts do-afters on the victim (a carry, the next grip) while it lasts.
    /// </summary>
    [DataField]
    public float ReelRate = 4f;

    /// <summary>
    ///     Popups when someone this grabber threw slams into a shove obstacle (a person, a table), instead of the
    ///         obstacle's own. Each gets <c>shover</c>, <c>target</c> and <c>obstacle</c>.
    /// </summary>
    [DataField]
    public KsAudiencePopups ThrowObstaclePopups =
        new("ks-grab-thrown-into-user", "ks-grab-thrown-into-target", "ks-grab-thrown-into-others");

    /// <summary>
    ///     Knockdown of someone this grabber threw when they slam into a wall or anything else static.
    /// </summary>
    [DataField]
    public TimeSpan ThrowWallKnockdownTime = TimeSpan.FromSeconds(3);

    [DataField]
    public SoundSpecifier? ThrowWallSound = new SoundCollectionSpecifier("MetalThud");

    /// <summary>
    ///     Popups for that slam into a wall. Each gets <c>shover</c> and <c>target</c>.
    /// </summary>
    [DataField]
    public KsAudiencePopups ThrowWallPopups =
        new("ks-grab-thrown-wall-user", "ks-grab-thrown-wall-target", "ks-grab-thrown-wall-others");

    /// <summary>
    ///     Popups for a slam into a wall whose knockdown doesn't take, such as while weightless.
    /// </summary>
    [DataField]
    public KsAudiencePopups ThrowWallResistedPopups = new("ks-grab-thrown-wall-resisted-user",
        "ks-grab-thrown-wall-resisted-target", "ks-grab-thrown-wall-resisted-others");

    /// <summary>
    ///     Damage to someone this grabber throws, or carries and throws, when they slam into a wall, a person or
    ///         anything solid.
    /// </summary>
    [DataField]
    public DamageSpecifier? ThrowImpactDamage = new() { DamageDict = { ["Blunt"] = 10 } };

    /// <summary>
    ///     Damage to a person someone thrown by this grabber slams into.
    /// </summary>
    [DataField]
    public DamageSpecifier? ThrowCollateralDamage = new() { DamageDict = { ["Blunt"] = 10 } };

    /// <summary>
    ///     tgstation's <c>CLICK_CD_GRABBING</c>: time between two tries to tighten the grip.
    /// </summary>
    [DataField]
    public TimeSpan UpgradeCooldown = TimeSpan.FromSeconds(1);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextUpgrade;

    /// <summary>
    ///     When the stage's damage is next dealt.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextDamage;
}
