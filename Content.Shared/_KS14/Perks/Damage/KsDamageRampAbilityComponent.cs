using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Perks.Damage;

/// <summary>
///     Each shot that hits a living mob makes the next shots from the same gun hit harder, up to a cap: once per shot,
///         however many of its pellets land. Lost on reloading, emptying the gun, dropping it or switching guns; a gun
///         that fills back up by itself (a recharging battery) keeps it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsDamageRampSystem))]
public sealed partial class KsDamageRampAbilityComponent : Component
{
    /// <summary>
    ///     Damage added per hit, as a fraction of the shot's own.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float BonusPerHit = 0.04f;

    /// <summary>
    ///     The most it can add, as a fraction.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MaxBonus = 0.4f;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public int Hits;

    /// <summary>
    ///     Counts resets, so a shot fired before one doesn't add to the ramp after it.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public int Generation;

    /// <summary>
    ///     The gun the ramp belongs to.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid? Gun;

    /// <summary>
    ///     The magazine (or the gun, if it holds its rounds itself) the last shot came from. Not networked: only ever
    ///         compared with the next shot's, on each side, and an ejected magazine can be deleted while it is still
    ///         remembered here.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? AmmoSource;

    /// <summary>
    ///     The gun's round count after the last shot; more than that before the next means it was reloaded, unless
    ///         the gun fills back up by itself.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public int LastCount;

    /// <summary>
    ///     Counts shots (trigger pulls), so every pellet or ray of one shot carries the same number.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public int Shot;

    /// <summary>
    ///     The last shot that added to the ramp: one shot adds once, however many of its pellets land. Server only.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public int CountedShot = -1;
}

/// <summary>
///     Marks a projectile fired under a <see cref="KsDamageRampAbilityComponent"/>, so its hit can add to the ramp.
/// </summary>
[RegisterComponent, Access(typeof(KsDamageRampSystem))]
public sealed partial class KsDamageRampShotComponent : Component
{
    [ViewVariables]
    public EntityUid Shooter;

    [ViewVariables]
    public int Generation;

    /// <summary>
    ///     The shot it was fired in (<see cref="KsDamageRampAbilityComponent.Shot"/>).
    /// </summary>
    [ViewVariables]
    public int Shot;
}
