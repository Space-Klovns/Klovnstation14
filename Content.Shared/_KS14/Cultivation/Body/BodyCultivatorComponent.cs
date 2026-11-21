using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._KS14.Cultivation.Body;

/// <summary>
///     The Body Molding Art: the other road. Instead of gathering qi, the flesh itself is tempered, part by part,
///         through nine stages from Copper Skin to the Primordial Chaos Body.
///     Training earns tempering; Forge the Body pours it into a part; once every part is tempered past the current
///         stage, the Tribulation of Flesh rebuilds the body one stage higher. Anyone can reach Copper Skin; going
///         further means committing to the path, which closes the way of qi for good.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(SharedBodyCultivationSystem))]
public sealed partial class BodyCultivatorComponent : Component
{
    /// <summary>
    ///     The alert that shows exhaustion from body arts.
    /// </summary>
    [DataField]
    public ProtoId<AlertPrototype> ExhaustionAlert = "KsBodyExhaustion";

    /// <summary>
    ///     The alert that shows banked tempering.
    /// </summary>
    [DataField]
    public ProtoId<AlertPrototype> TemperingAlert = "KsBodyTempering";

    public const int MaxStage = 9;
    public const int MortalStageCap = 1;

    [DataField, AutoNetworkedField]
    public int Stage;

    /// <summary>
    ///     Read the Body Molding Art or was accepted as a disciple. Mortals stop at Copper Skin.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Committed;

    /// <summary>
    ///     Earned from training but not yet forged into a part.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Tempering;

    [DataField]
    public float TemperingCap = 120f;

    /// <summary>
    ///     Body arts tire the body; it drains back on its own.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Exhaustion;

    [DataField]
    public float MaxExhaustion = 100f;

    [DataField, AutoNetworkedField]
    public Dictionary<BodyGroup, int> Levels = new();

    [DataField, AutoNetworkedField]
    public Dictionary<BodyGroup, int> Progress = new();

    /// <summary>
    ///     Powers unlocked by the current levels.
    /// </summary>
    [DataField, AutoNetworkedField]
    public HashSet<ProtoId<BodyTemperingPowerPrototype>> Powers = new();

    // Combined effects of the stage and every unlocked power, recalculated whenever a level or the stage changes.

    [DataField, AutoNetworkedField]
    public Dictionary<string, float> DamageMultipliers = new();

    [DataField, AutoNetworkedField]
    public float SpeedMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float StaminaMultiplier = 1f;

    [DataField]
    public float HungerMultiplier = 1f;

    [DataField]
    public float RegenPerSecond;

    [DataField]
    public float HealthBonus;

    [DataField]
    public float PunchDamage;

    [DataField]
    public float PunchStaminaDamage;

    [DataField]
    public float PunchKnockdownChance;

    [DataField]
    public float PunchThrowChance;

    [DataField]
    public float PunchStructuralDamage;

    [DataField]
    public bool PunchBreaksWalls;

    [DataField]
    public bool EternalHeart;

    /// <summary>
    ///     The thresholds for critical condition and death before any bonus, so bonuses can be reapplied cleanly.
    /// </summary>
    [DataField]
    public float? BaseCriticalThreshold;

    [DataField]
    public float? BaseDeadThreshold;

    /// <summary>
    ///     Body arts granted, keyed by action prototype.
    /// </summary>
    [DataField]
    public Dictionary<EntProtoId, EntityUid> Techniques = new();

    /// <summary>
    ///     Training source -> when it can next earn tempering.
    /// </summary>
    [DataField]
    public Dictionary<string, TimeSpan> TrainingCooldowns = new();

    /// <summary>
    ///     Who this body cultivator has beaten down lately, and when beating them teaches anything again.
    ///         Server-side only, so friends can't farm each other.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> RecentlyDefeated = new();

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextTick;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextPassiveTempering;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan EternalHeartReady;

    [DataField]
    public TimeSpan EternalHeartCooldown = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     Bone-cracking cycles left in a running Tribulation of Flesh, and its chance of success.
    /// </summary>
    [DataField]
    public int TribulationCyclesLeft;

    [DataField]
    public int TribulationReadiness;

    /// <summary>
    ///     When a dead body's Eternal Heart restarts it, if it's going to.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan? EternalHeartReviveAt;

    /// <summary>
    ///     Cooldowns of granted arts as their prototypes set them, before stage shortens them.
    /// </summary>
    [DataField]
    public Dictionary<EntProtoId, TimeSpan> BaseCooldowns = new();

    /// <summary>
    ///     The part Forge the Body is pouring into.
    /// </summary>
    [DataField]
    public BodyGroup? ForgeTarget;
}
