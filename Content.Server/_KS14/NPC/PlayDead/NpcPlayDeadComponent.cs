using Content.Server._KS14.NPC.Meters;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.PlayDead;

/// <summary>
///     Lets an NPC that has been left on its own and badly rattled play dead: it lies still until a hostile comes into
///         view, then gets up shooting. Decided by <see cref="NpcPlayDeadSystem"/>; carried out by the
///         <c>PlayDeadOperator</c> HTN branch.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcPlayDeadSystem))]
public sealed partial class NpcPlayDeadComponent : Component
{
    /// <summary>
    ///     The meter that has to be high - caution.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<NpcMeterPrototype> Meter;

    /// <summary>
    ///     How high <see cref="Meter"/> has to be.
    /// </summary>
    [DataField]
    public float Threshold = 95f;

    /// <summary>
    ///     The chance, from 0 to 1, of playing dead when the meter first gets that high with the NPC on its own. Rolled
    ///         once each time: until the meter falls below <see cref="Threshold"/> again, a lost roll stays lost.
    /// </summary>
    [DataField]
    public float Chance = 0.35f;

    /// <summary>
    ///     How long it lies there, at most, if nobody comes.
    /// </summary>
    [DataField]
    public TimeSpan MaxDuration = TimeSpan.FromSeconds(120);

    [ViewVariables]
    public bool Playing;

    [ViewVariables]
    public TimeSpan PlayingSince;

    /// <summary>
    ///     Whether this spell of high caution has been rolled for already.
    /// </summary>
    [ViewVariables]
    public bool Rolled;
}
