using Content.Shared.Damage;
using Content.Shared.Physics;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Weapons.Ranged;

// damage intentionally not dependent on distance

/// <summary>
///    Makes a literal cone of backblast when firing this ammo, on the shooter/gun.
///         This is added to the affected gun.
///
///     The area of effect is a circular sector originating from the shooter, of
///         <see cref="Radius"/> metres radius, with an angle of <see cref="EffectField"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GunBackblastComponent : Component
{
    #region Cone

    /// <summary>
    ///     Direction of backblast relative to firing direction. This is
    ///         directly added onto firing direction.
    /// </summary>
    [DataField]
    public Angle DirectionOffset = Angle.Zero;

    /// <summary>
    ///     Radius of the backblast - if your distance from the shooter
    ///         is farther than this, you won't get hit.
    /// </summary>
    [DataField(required: true)]
    public float Radius = 0f;

    /// <summary>
    ///     Angle of the circular sector of effect.
    /// </summary>
    [DataField]
    public Angle EffectField = 0f;

    #endregion
    #region Damage

    [DataField]
    public DamageSpecifier Damage = new();

    [DataField]
    public float PushForce = 100f;

    [DataField]
    public TimeSpan KnockdownTime = TimeSpan.Zero;

    #endregion

    [DataField]
    public CollisionGroup CollisionGroup = CollisionGroup.BulletImpassable;

    /// <summary>
    ///     From 0 - 1: the chance for tiles in the AOE to break.
    /// </summary>
    [DataField]
    public float TilebreakChance = 0f;
}
