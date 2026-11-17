using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Meters;

/// <summary>
///     Raises a meter on this NPC as it takes damage, and lowers it as it heals. With anger as the meter, a boss gets
///         angrier the more it is hurt. See <see cref="NpcMeterOnDamageSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class NpcMeterOnDamageComponent : Component
{
    [DataField(required: true)]
    public ProtoId<NpcMeterPrototype> Meter;

    /// <summary>
    ///     Added per point of damage taken, and taken away per point healed.
    /// </summary>
    [DataField]
    public float PerDamage = 1f;
}
