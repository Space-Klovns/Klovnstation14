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
