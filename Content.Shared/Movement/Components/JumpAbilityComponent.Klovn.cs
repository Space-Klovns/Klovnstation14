using Content.Shared.Actions;

namespace Content.Shared.Movement.Components;

public sealed partial class JumpAbilityComponent
{
    /// <summary>
    /// The duration of the knockdown after finishing a jump, when <see cref="KnockdownOnFinish"/>
    /// is true.
    ///
    /// Not applied after a collision with something that gets knocked down, if <see cref="CanCollide"/> is true or this is null.
    /// </summary>
    [DataField]
    public TimeSpan? PunishKnockdown = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Damage dealt to hit entities if <see cref="CanCollide"/> is true.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float HitStaminaDamage = 60f;

    /// <summary>
    /// Knockdown duration dealt to hit entities if <see cref="CanCollide"/> is true.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? HitKnockdownDuration = null;
}

public interface IKsGravityJumpEvent
{
    float StaminaCost { get; set; }
}

public sealed partial class KsGravityJumpWorldEvent : WorldTargetActionEvent, IKsGravityJumpEvent
{
    // KS14 addition
    /// <summary>
    /// Amount of stamina taken when doing this action.
    /// </summary>
    [DataField]
    public float StaminaCost { get; set; }
}

// KS14
/// <summary>
///     Raised on the target after it gets hit by
///         something that was jumping and couldve been hurt.
/// </summary>
[ByRefEvent]
public readonly record struct KsHitByJumpEvent(EntityUid ActorUid);
