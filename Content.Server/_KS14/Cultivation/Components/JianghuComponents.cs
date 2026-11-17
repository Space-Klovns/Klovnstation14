using Content.Server._KS14.Cultivation.Systems;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._KS14.Cultivation.Components;

/// <summary>
///     Standing in the jianghu, the martial world. Lives on the mind, so it survives a change of body.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(JianghuSystem))]
public sealed partial class JianghuFaceComponent : Component
{
    [DataField]
    public int Face;

    /// <summary>
    ///     Until when everyone remembers they interfered in someone else's duel.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan DishonoredUntil;

    /// <summary>
    ///     Winning or watching duels only teaches so fast.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextDuelInsight;

    /// <summary>
    ///     Until when everyone remembers they betrayed a sworn sibling.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan OathbrokenUntil;

    /// <summary>
    ///     Anyone can swear brotherhood, but only once a round.
    /// </summary>
    [DataField]
    public bool OathSworn;
}

/// <summary>
///     Marks a body whose mind has a name in the jianghu, so examining it says so.
/// </summary>
[RegisterComponent, Access(typeof(JianghuSystem))]
public sealed partial class JianghuExamineComponent : Component;

/// <summary>
///     One side of an honor duel in progress.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(JianghuSystem))]
public sealed partial class HonorDuelComponent : Component
{
    public const string StakeContainer = "honor_duel_stake";

    [DataField]
    public EntityUid Opponent;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan EndsAt;

    [DataField]
    public EntityUid? YieldAction;

    /// <summary>
    ///     Set once the duel has been decided, so the other side doesn't decide it again.
    /// </summary>
    [DataField]
    public bool Ended;
}

/// <summary>
///     A challenge that hasn't been answered yet.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(JianghuSystem))]
public sealed partial class DuelChallengeComponent : Component
{
    [DataField]
    public EntityUid Target;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ExpiresAt;
}

/// <summary>
///     Someone has been challenged and not answered.
/// </summary>
[RegisterComponent, Access(typeof(JianghuSystem))]
public sealed partial class DuelChallengedComponent : Component
{
    [DataField]
    public EntityUid Challenger;
}

public enum SectMission : byte
{
    Pills,
    Duel,
    Meditate,
    Recruit,
    HeartDemon,
}

/// <summary>
///     A sect. It lives on an entity of its own outside the station, so it outlasts its plaque.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(JianghuSystem))]
public sealed partial class SectComponent : Component
{
    [DataField]
    public string SectName = string.Empty;

    /// <summary>
    ///     The master's mind.
    /// </summary>
    [DataField]
    public EntityUid Master;

    /// <summary>
    ///     Every member's mind, the master's included.
    /// </summary>
    [DataField]
    public HashSet<EntityUid> Members = new();

    /// <summary>
    ///     Other sects sworn as rivals.
    /// </summary>
    [DataField]
    public HashSet<EntityUid> Rivals = new();

    [DataField]
    public EntityUid? Plaque;

    /// <summary>
    ///     The ruler this sect has sworn fealty to (their body), if any. Members cultivate faster under their rule.
    /// </summary>
    [DataField]
    public EntityUid? Liege;

    [DataField]
    public SectMission? Mission;

    [DataField]
    public int MissionGoal;

    [DataField]
    public int MissionProgress;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan MissionDeadline;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextMission;

    [DataField]
    public int MissionsCompleted;

    /// <summary>
    ///     Swords issued at the plaque; one per member.
    /// </summary>
    [DataField]
    public int JianIssued;
}

/// <summary>
///     A sect's gate. Others ask to join here; the master proclaims tournaments here.
/// </summary>
[RegisterComponent, Access(typeof(JianghuSystem))]
public sealed partial class SectPlaqueComponent : Component
{
    [DataField]
    public EntityUid? Sect;

    /// <summary>
    ///     Who is waiting on the master's answer.
    /// </summary>
    [DataField]
    public EntityUid? Petitioner;
}

/// <summary>
///     On a mind: the sect it belongs to.
/// </summary>
[RegisterComponent, Access(typeof(JianghuSystem))]
public sealed partial class SectMemberComponent : Component
{
    [DataField]
    public EntityUid Sect;

    [DataField]
    public EntityUid? TransmissionAction;
}

/// <summary>
///     Sworn siblings: "We were not born on the same day, but we will die on the same day." Lives on an entity of its
///         own, like a sect.
/// </summary>
[RegisterComponent, Access(typeof(JianghuSystem))]
public sealed partial class SwornBondComponent : Component
{
    public const int MaxMembers = 3;

    /// <summary>
    ///     The siblings' minds.
    /// </summary>
    [DataField]
    public HashSet<EntityUid> Members = new();

    /// <summary>
    ///     When each sibling last struck another, by striker mind.
    /// </summary>
    [DataField]
    public Dictionary<EntityUid, List<TimeSpan>> Strikes = new();

    /// <summary>
    ///     When each sibling's next "badly hurt" warning may go out, by mind.
    /// </summary>
    [DataField]
    public Dictionary<EntityUid, TimeSpan> HurtWarnings = new();
}

/// <summary>
///     On a mind: the oath it is sworn to.
/// </summary>
[RegisterComponent, Access(typeof(JianghuSystem))]
public sealed partial class SwornMemberComponent : Component
{
    [DataField]
    public EntityUid Bond;
}

/// <summary>
///     A bundle of incense. Lit, it can be offered to someone to swear brotherhood.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(JianghuSystem))]
public sealed partial class SwornIncenseComponent : Component
{
    [DataField]
    public bool Lit;

    [DataField]
    public TimeSpan BurnTime = TimeSpan.FromMinutes(2);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan BurnsOutAt;

    [DataField]
    public EntityUid? Offerer;

    [DataField]
    public EntityUid? Offeree;
}

/// <summary>
///     A sworn sibling fights beside them: they tire less and strike harder.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(JianghuSystem))]
public sealed partial class SwornTogetherComponent : Component
{
    [DataField]
    public float StaminaMultiplier = 0.75f;

    [DataField]
    public float BonusDamage = 2f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ExpiresAt;
}

/// <summary>
///     A sworn sibling is dead. Grief turns to fury.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(JianghuSystem))]
public sealed partial class SwornVengeanceComponent : Component
{
    [DataField]
    public float StaminaMultiplier = 0.5f;

    [DataField]
    public float BonusDamage = 5f;

    [DataField]
    public float SpeedMultiplier = 1.2f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ExpiresAt;
}

[RegisterComponent, Access(typeof(WuxiaWeaponSystem))]
public sealed partial class MeteorHammerComponent : Component
{
    [DataField]
    public float PullChance = 0.35f;
}

[RegisterComponent, Access(typeof(WuxiaWeaponSystem))]
public sealed partial class RopeDartComponent : Component
{
    [DataField]
    public float Range = 4f;

    [DataField]
    public float Damage = 8f;

    [DataField]
    public float DisarmChance = 0.4f;
}

[RegisterComponent, Access(typeof(WuxiaWeaponSystem))]
public sealed partial class BambooStaffComponent : Component
{
    [DataField]
    public float SweepChance = 0.3f;

    [DataField]
    public TimeSpan SweepDuration = TimeSpan.FromSeconds(1.5);

    /// <summary>
    ///     Sweeps left before the bamboo splits.
    /// </summary>
    [DataField]
    public int SweepsLeft = 6;
}

/// <summary>
///     A sword issued at a sect's plaque, one per member.
/// </summary>
[RegisterComponent, Access(typeof(WuxiaWeaponSystem), typeof(JianghuSystem))]
public sealed partial class SectJianComponent : Component
{
    [DataField]
    public EntityUid? Sect;

    [DataField]
    public float MemberBonusDamage = 5f;

    [DataField]
    public float MemberStaminaDamage = 8f;
}

[RegisterComponent, Access(typeof(WuxiaWeaponSystem))]
public sealed partial class AcupointNeedleComponent : Component
{
    [DataField]
    public TimeSpan SlowDuration = TimeSpan.FromSeconds(2);

    [DataField]
    public float SlowMultiplier = 0.5f;
}

[RegisterComponent, Access(typeof(WuxiaWeaponSystem))]
public sealed partial class FlyingDaggerComponent : Component
{
    [DataField]
    public float BonusPerRealm = 3f;
}

[RegisterComponent, Access(typeof(WuxiaWeaponSystem))]
public sealed partial class SmokePelletComponent : Component
{
    [DataField]
    public Robust.Shared.Prototypes.EntProtoId Smoke = "KsSmokePelletCloud";
}
