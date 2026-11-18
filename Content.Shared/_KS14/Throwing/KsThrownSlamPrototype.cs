using Content.Shared._KS14.KsPopup;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Throwing;

/// <summary>
///     What happens to someone sent flying when they slam into something: how long a wall puts them down, how much
///         it hurts, what is said. Whatever throws people picks one for <see cref="KsThrownSlamSystem.MarkThrown"/>:
///         a grab's throw, and anything else that should slam people into things, such as a blast.
/// </summary>
[Prototype]
public sealed partial class KsThrownSlamPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     Popups when they slam into a shove obstacle (a person, a table), instead of the obstacle's own. Each gets
    ///         <c>shover</c>, <c>target</c> and <c>obstacle</c>. With nobody behind the throw, the thrown gets the
    ///         target's message and everyone else the others'.
    /// </summary>
    [DataField]
    public KsAudiencePopups ObstaclePopups;

    /// <summary>
    ///     Knockdown when they slam into a wall or anything else static.
    /// </summary>
    [DataField]
    public TimeSpan WallKnockdownTime = TimeSpan.FromSeconds(3);

    [DataField]
    public SoundSpecifier? WallSound;

    /// <summary>
    ///     Popups for that slam into a wall. Each gets <c>shover</c> and <c>target</c>.
    /// </summary>
    [DataField]
    public KsAudiencePopups WallPopups;

    /// <summary>
    ///     Popups for a slam into a wall whose knockdown doesn't take, such as while weightless.
    /// </summary>
    [DataField]
    public KsAudiencePopups WallResistedPopups;

    /// <summary>
    ///     Damage to the thrown when they slam into a wall, a person or anything solid.
    /// </summary>
    [DataField]
    public DamageSpecifier? ImpactDamage;

    /// <summary>
    ///     Damage to a person they slam into.
    /// </summary>
    [DataField]
    public DamageSpecifier? CollateralDamage;

    /// <summary>
    ///     Whether the slam stops them dead, rather than leaving them to slide or fly on past what they hit.
    /// </summary>
    [DataField]
    public bool StopsMomentum = true;

    /// <summary>
    ///     How much of their momentum goes to a person they slam into, who is pushed on by it: 1 is all of it. Only
    ///         someone in the way takes it (an obstacle with a <c>CollateralKnockdownTime</c>); a table or a wall
    ///         goes nowhere, and loose things are already pushed by the collision itself.
    /// </summary>
    [DataField]
    public float MomentumTransfer = 1f;
}
