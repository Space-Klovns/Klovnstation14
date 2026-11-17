using Content.Shared.Damage;
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
