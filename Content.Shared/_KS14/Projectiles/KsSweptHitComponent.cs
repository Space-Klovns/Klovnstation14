namespace Content.Shared._KS14.Projectiles;

/// <summary>
///     On a round that hit what it was aimed at by sweeping its path, rather than by touching it in a physics step: the
///         contact it may still make with it is not a second hit. See <c>PredictedProjectileSystem.Klovn.Sweep.cs</c>.
/// </summary>
[RegisterComponent]
public sealed partial class KsSweptHitComponent : Component
{
    [ViewVariables]
    public EntityUid HitUid;
}
