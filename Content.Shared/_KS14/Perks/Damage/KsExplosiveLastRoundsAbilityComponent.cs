using Content.Shared.Explosion;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Perks.Damage;

/// <summary>
///     The last rounds of a magazine that went in full explode where they hit. How many is
///         <see cref="Fraction"/> of the magazine, at least <see cref="MinRounds"/>. A magazine that was already
///         partly spent when it went in gets none.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsExplosiveLastRoundsSystem))]
public sealed partial class KsExplosiveLastRoundsAbilityComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Fraction = 0.05f;

    [DataField, AutoNetworkedField]
    public int MinRounds = 1;

    [DataField, AutoNetworkedField]
    public ProtoId<ExplosionPrototype> ExplosionType = "KsAntiPersonnelRocket";

    [DataField, AutoNetworkedField]
    public float TotalIntensity = 6f;

    [DataField, AutoNetworkedField]
    public float IntensitySlope = 2f;

    [DataField, AutoNetworkedField]
    public float MaxIntensity = 2f;

    /// <summary>
    ///     The colour an explosive round's projectile is drawn in.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Color TracerColor = Color.FromHex("#ff5a1f");

    /// <summary>
    ///     Played as an explosive round is fired, on top of the gun's own sound.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SoundSpecifier? FireSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/grenade_launcher.ogg");

    // Not networked, these two: only ever compared with the next shot's gun and magazine, on each side, and either can
    //      be deleted while still remembered here, which the state would trip over.
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? Gun;

    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? AmmoSource;

    /// <summary>
    ///     Rounds the magazine held when it went in.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public int Capacity;

    /// <summary>
    ///     Whether the magazine went in full.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public bool Eligible;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public int LastCount;

    /// <summary>
    ///     Whether the shot being fired now is one of the explosive rounds.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public bool ArmedShot;
}

/// <summary>
///     A projectile that explodes where it hits. Networked so everyone sees it in its tracer colour.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(KsExplosiveLastRoundsSystem))]
public sealed partial class KsExplosiveShotComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public Color TracerColor = Color.White;

    [ViewVariables]
    public EntityUid? Shooter;

    [ViewVariables]
    public ProtoId<ExplosionPrototype> ExplosionType = "KsAntiPersonnelRocket";

    [ViewVariables]
    public float TotalIntensity;

    [ViewVariables]
    public float IntensitySlope;

    [ViewVariables]
    public float MaxIntensity;

    [ViewVariables]
    public bool Exploded;
}
