namespace Content.Server._KS14.Projectiles;

/// <summary>
/// Marks a temporary, invisible physics body standing in for a lag-compensated target's rewound
/// position. Isolated (via PreventCollideEvent) so it only ever collides with <see cref="Projectile"/>;
/// when that collision happens, the hit is redirected onto the real <see cref="Target"/> it mimics.
/// Re-rewound every tick to stay <see cref="LagDuration"/> behind <see cref="Target"/>'s real position,
/// rather than staying frozen at its spawn-time snapshot.
/// </summary>
[RegisterComponent]
public sealed partial class LagCompensationGhostComponent : Component
{
    [ViewVariables]
    public EntityUid Projectile;

    [ViewVariables]
    public EntityUid Target;

    [ViewVariables]
    public TimeSpan LagDuration;
}
