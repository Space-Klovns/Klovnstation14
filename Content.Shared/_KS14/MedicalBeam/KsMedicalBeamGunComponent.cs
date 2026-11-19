using Content.Shared.Damage.Prototypes;
using Content.Shared.Explosion;
using Content.Shared.FixedPoint;
using Content.Shared.Guidebook;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.MedicalBeam;

/// <summary>
///     A medical beam gun, ported from tgstation's <c>/obj/item/gun/medbeam</c>: fired at a living mob, friend or not,
///         it locks a healing beam onto them, which heals every <see cref="TickInterval"/> and breaks, with a warning,
///         once they are out of <see cref="MaxRange"/> or out of sight. Fired at anything else it lets go. It fires
///         nothing. Two beams held by different people that cross blow up where they cross (<see cref="CrossExplosion"/>).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(KsMedicalBeamSystem))]
public sealed partial class KsMedicalBeamGunComponent : Component
{
    /// <summary>
    ///     How far the beam reaches, in metres (tg: <c>max_range = 8</c> tiles).
    /// </summary>
    [DataField, AutoNetworkedField, GuidebookData]
    public float MaxRange = 8f;

    /// <summary>
    ///     How often it heals (tg: <c>check_delay = 10</c>, a second).
    /// </summary>
    [DataField]
    public TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How much of each damage group each tick heals, spread evenly over the group's types (as tg's
    ///         <c>adjust_brute_loss</c> and the like).
    /// </summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<DamageGroupPrototype>, FixedPoint2> Heal = new();

    /// <summary>
    ///     Spawned on the patient, riding along with them, each tick that heals someone hurt (tg's cyan heal sparkle).
    /// </summary>
    [DataField]
    public EntProtoId? HealEffect;

    /// <summary>
    ///     A click that hits no mob at all still takes the nearest mob it can beam this close to it, in metres.
    /// </summary>
    [DataField]
    public float AimAssistRadius = 0.6f;

    /// <summary>
    ///     What goes off where this beam crosses one held by someone else, checked each <see cref="TickInterval"/>
    ///         (tg's <c>mid_los_check</c>: <c>explosion(heavy_impact_range = 3, light_impact_range = 5, flash_range = 8)</c>).
    ///         Null: crossing does nothing.
    /// </summary>
    [DataField]
    public ProtoId<ExplosionPrototype>? CrossExplosion = "Default";

    /// <summary>
    ///     How far the crossing explosion reaches, in metres (tg's light impact, 5 tiles, plus the faint edge).
    /// </summary>
    [DataField]
    public float CrossExplosionRadius = 5.5f;

    /// <summary>
    ///     How fast its intensity climbs from the edge inwards, per metre.
    /// </summary>
    [DataField]
    public float CrossExplosionSlope = 3f;

    /// <summary>
    ///     Its intensity on any one tile at most: 8, about 120 damage to a mob with no armour, as tg's heavy impact
    ///         (60 brute and 60 burn) inside 3 tiles.
    /// </summary>
    [DataField]
    public float CrossExplosionMaxIntensity = 8f;

    [DataField]
    public SoundSpecifier? StartSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/laser_cannon2.ogg", AudioParams.Default.WithVolume(-6f));

    [DataField]
    public SoundSpecifier? LostSound = new SoundPathSpecifier("/Audio/Machines/buzz-sigh.ogg");

    /// <summary>
    ///     The beam the clients draw between holder and target, a vertical strip one metre long, repeated.
    /// </summary>
    [DataField]
    public SpriteSpecifier.Rsi BeamSprite = new(new ResPath("/Textures/_KS14/Effects/MedicalBeam/medical_beam.rsi"), "medbeam");

    /// <summary>
    ///     Who the beam is on; null while it is off.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid? Target;

    /// <summary>
    ///     Who is holding the beam, where it starts.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid? User;

    [ViewVariables(VVAccess.ReadOnly), AutoPausedField]
    public TimeSpan NextTick;
}
