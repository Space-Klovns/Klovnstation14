using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Projectiles;

/// <summary>
/// Tunes the damage falloff of the projectiles a gun fires, on top of each bullet's own
/// <see cref="KsProjectileFalloffComponent"/>. Applied once, when the projectile is fired, by writing the
/// result onto the projectile. An override replaces the bullet's value, then <see cref="RangeScale"/>
/// multiplies both ranges (so a longer barrel is just a scale above 1).
/// Having this component is what turns falloff on for a gun: a bullet without its own
/// <see cref="KsProjectileFalloffComponent"/> gets one when fired, starting from that component's defaults.
/// A gun without this component leaves its bullets as they are. No separate enabled flag: remove the component to
/// turn it off.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsGunProjectileFalloffComponent : Component
{
    /// <summary>Replaces the bullet's <see cref="KsProjectileFalloffComponent.FullDamageRange"/> when set.</summary>
    [DataField]
    public float? FullDamageRange;

    /// <summary>Replaces the bullet's <see cref="KsProjectileFalloffComponent.MinDamageRange"/> when set.</summary>
    [DataField]
    public float? MinDamageRange;

    /// <summary>Replaces the bullet's <see cref="KsProjectileFalloffComponent.MinMultiplier"/> when set. 1 disables falloff.</summary>
    [DataField]
    public float? MinMultiplier;

    /// <summary>Multiplies both ranges, after any override.</summary>
    [DataField]
    public float RangeScale = 1f;
}
