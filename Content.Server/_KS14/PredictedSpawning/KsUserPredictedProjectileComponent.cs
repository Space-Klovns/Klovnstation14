namespace Content.Server._KS14.PredictedSpawning;

/// <summary>
///     A projectile that already existed when a user's own predicted request shot it (an arrow, say), so that user's
///         client predicted its flight and hit itself and is not sent the hit's impact. Overwritten by the next such
///         shot, and only trusted while <see cref="User"/> is still the projectile's shooter.
/// </summary>
[RegisterComponent, UnsavedComponent, Access(typeof(KsPredictedSpawnSystem))]
public sealed partial class KsUserPredictedProjectileComponent : Component
{
    [ViewVariables]
    public EntityUid User;
}
