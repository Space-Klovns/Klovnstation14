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
    public float StunTime = 1.0f; // KS14: 2.0 -> 1.0, a slam through glass is a short stun on top of the knockdown
}
