namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     Makes an NPC ranged shooter (a turret) track a moving target so it cannot be out-run by strafing.
/// </summary>
[RegisterComponent]
public sealed partial class KsNpcAimTrackingComponent : Component
{
    /// <summary>
    ///     Fraction of the target's angular velocity (as seen from the shooter, in rad/s) added on top of the
    ///         base rotation speed. At 1, the base speed is purely catch-up speed.
    /// </summary>
    [DataField]
    public float AngularVelocityCompensation = 1f;

    /// <summary>
    ///     Hard clamp on the total rotation speed per second. Null means no clamp.
    /// </summary>
    [DataField]
    public Angle? MaxRotationSpeed;

    /// <summary>
    ///     Keep aiming during the shoot delay and lock-on wait.
    /// </summary>
    [DataField]
    public bool RotateWhileWaiting = true;

    /// <summary>
    ///     Overrides the ranged combat accuracy threshold. Null keeps the ranged combat component's value.
    /// </summary>
    [DataField]
    public Angle? AccuracyThreshold;

    [ViewVariables]
    public Angle? LastGoalAngle;

    [ViewVariables]
    public EntityUid? LastTarget;
}
