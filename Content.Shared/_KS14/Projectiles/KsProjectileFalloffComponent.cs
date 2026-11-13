using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._KS14.Projectiles;

/// <summary>
/// Scales a projectile's damage down with the distance it has flown: full damage up to
/// <see cref="FullDamageRange"/> tiles from where it was fired, a linear fall to
/// <see cref="MinMultiplier"/> at <see cref="MinDamageRange"/> tiles, flat beyond.
/// Whether it applies depends on two cvars: <c>klovn.projectile.damage_falloff</c> off means never; on, it applies
/// to every projectile with this component if <c>klovn.projectile.damage_falloff_everywhere</c> is on, and otherwise
/// only if the firing gun has a <see cref="KsGunProjectileFalloffComponent"/> (<see cref="GunOptedIn"/>).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KsProjectileFalloffComponent : Component
{
    /// <summary>Tiles from the origin up to which the projectile does full damage.</summary>
    [DataField, AutoNetworkedField]
    public float FullDamageRange = 8f;

    /// <summary>Tiles from the origin at which the damage reaches <see cref="MinMultiplier"/>.</summary>
    [DataField, AutoNetworkedField]
    public float MinDamageRange = 20f;

    /// <summary>The damage multiplier at and beyond <see cref="MinDamageRange"/>.</summary>
    [DataField, AutoNetworkedField]
    public float MinMultiplier = 0.5f;

    /// <summary>
    /// Where the projectile was fired. Set at fire time, not a data field, so a spawned prototype stays
    /// identical to its YAML. Unset (<see cref="MapId.Nullspace"/>) means no falloff.
    /// </summary>
    [ViewVariables, AutoNetworkedField, Access(typeof(KsProjectileFalloffSystem))]
    public MapId OriginMap = MapId.Nullspace;

    [ViewVariables, AutoNetworkedField, Access(typeof(KsProjectileFalloffSystem))]
    public Vector2 OriginPosition;

    /// <summary>Set at fire time when the gun has a <see cref="KsGunProjectileFalloffComponent"/>.</summary>
    [ViewVariables, AutoNetworkedField, Access(typeof(KsProjectileFalloffSystem))]
    public bool GunOptedIn;
}
