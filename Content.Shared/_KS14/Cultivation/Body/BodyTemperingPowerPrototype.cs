using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Cultivation.Body;

/// <summary>
///     The ten parts of the body the Body Molding Art tempers: four outer and six inner.
/// </summary>
public enum BodyGroup : byte
{
    Head,
    Chest,
    Arms,
    Legs,
    Eyes,
    Ears,
    Heart,
    Lungs,
    Liver,
    Stomach,
}

/// <summary>
///     One power a body part gains when tempered to a level. Every group has nine.
///     Numeric effects from all unlocked powers combine: for damage, stamina and hunger the best (lowest)
///     multiplier wins; for speed the best (highest); regeneration, health and punch bonuses add up.
/// </summary>
[Prototype]
public sealed partial class BodyTemperingPowerPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public BodyGroup Group;

    [DataField(required: true)]
    public int Level;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    /// <summary>
    ///     A power that changes how you play (a new sense, an immunity, a new kind of blow) rather than a small number.
    ///         The body powers window highlights these.
    /// </summary>
    [DataField]
    public bool Major;

    /// <summary>
    ///     Damage type -> multiplier on damage taken.
    /// </summary>
    [DataField]
    public Dictionary<string, float> DamageMultipliers = new();

    [DataField]
    public float SpeedMultiplier = 1f;

    [DataField]
    public float StaminaMultiplier = 1f;

    [DataField]
    public float HungerMultiplier = 1f;

    /// <summary>
    ///     Brute and burn healed every second.
    /// </summary>
    [DataField]
    public float RegenPerSecond;

    /// <summary>
    ///     Added to the damage thresholds for critical condition and death.
    /// </summary>
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

    /// <summary>
    ///     Extra structural damage punches deal to objects; walls take it too from Mountain-Breaking Fists.
    /// </summary>
    [DataField]
    public float PunchStructuralDamage;

    [DataField]
    public bool PunchBreaksWalls;

    /// <summary>
    ///     Eternal Heart: death restarts the heart, once in a while.
    /// </summary>
    [DataField]
    public bool EternalHeart;

    [DataField]
    public ComponentRegistry Components = new();

    /// <summary>
    ///     Whether this power's components replace ones an earlier power already gave (a stronger night vision, say)
    ///         instead of leaving them as they are.
    /// </summary>
    [DataField]
    public bool ReplaceComponents;

    /// <summary>
    ///     Names of components the power takes away when unlocked, e.g. Respirator, the need to breathe.
    /// </summary>
    [DataField]
    public List<string> RemoveComponents = new();
}
