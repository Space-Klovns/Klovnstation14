using Content.Shared.Damage;

namespace Content.Shared.Climbing.Components;

/// <summary>
///     Glass tables shatter and stun you when climbed on.
///     This is a really entity-specific behavior, so opted to make it
///     not very generalized with regards to naming.
/// </summary>
[RegisterComponent, Access(typeof(Systems.ClimbSystem))]
public sealed partial class GlassTableComponent : Component
{
    /// <summary>
    ///     How much damage should be given to the climber?
    /// </summary>
    [DataField("climberDamage")]
    public DamageSpecifier ClimberDamage = default!;

    /// <summary>
    ///     How much damage should be given to the table when climbed on?
    /// </summary>
    [DataField("tableDamage")]
    public DamageSpecifier TableDamage = default!;

    /// <summary>
    ///     How much mass should be needed to break the table?
    /// </summary>
    [DataField("tableMassLimit")]
    public float MassLimit;

    /// <summary>
    ///     How long should someone who climbs on this table be stunned for?
    /// </summary>
    [DataField] // KS14: made a data field
    public float StunTime = 2.0f;

    /// <summary>
    ///     KS14: the stun for someone put on the table by somebody else, as by a shove or a throw: a short one, on top
    ///         of the knockdown they get from that.
    /// </summary>
    [DataField]
    public float KsPlacedStunTime = 1.0f;
}
