namespace Content.Server._KS14.Projectiles;

/// <summary>
/// Tracks the lag-compensation ghosts spawned for a shot and the real entities they stand in for.
/// The projectile ignores every entity in <see cref="IgnoredRealTargets"/> so a target is never hit
/// twice - once through its ghost and once for real.
/// </summary>
[RegisterComponent]
public sealed partial class LagCompensatingProjectileComponent : Component
{
    [ViewVariables]
    public List<EntityUid> Ghosts = new();

    [ViewVariables]
    public HashSet<EntityUid> IgnoredRealTargets = new();
}
