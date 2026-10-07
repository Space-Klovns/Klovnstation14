using Content.Shared._KS14.GenericSpriteFlick;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     How an attack's projectiles are aimed, burst by burst. See <see cref="Systems.NpcCombatRangedPatternSystem"/>.
/// </summary>
public enum NpcRangedType
{
    /// <summary>
    ///     <see cref="NpcRangedAttackPatternComponent.Shots"/> straight at the target.
    /// </summary>
    Single,

    /// <summary>
    ///     A stream that turns by <see cref="NpcRangedAttackPatternComponent.DegreesPerShot"/> every burst.
    /// </summary>
    Spiral,

    /// <summary>
    ///     Two <see cref="Spiral"/>s, <see cref="NpcRangedAttackPatternComponent.RotationOffset"/> apart.
    /// </summary>
    DoubleSpiral,

    /// <summary>
    ///     A fan of <see cref="NpcRangedAttackPatternComponent.Shots"/> across
    ///         <see cref="NpcRangedAttackPatternComponent.Spread"/>, towards the target, or the way the NPC faces.
    /// </summary>
    Shotgun,

    /// <summary>
    ///     Up, down, left and right, then the diagonals, burst about.
    /// </summary>
    CardinalDirections,

    /// <summary>
    ///     The four diagonals.
    /// </summary>
    DiagonalDirections,

    /// <summary>
    ///     All eight directions.
    /// </summary>
    AllDirections,

    /// <summary>
    ///     <see cref="NpcRangedAttackPatternComponent.Shots"/> in random directions.
    /// </summary>
    RandomAoe,

    /// <summary>
    ///     As <see cref="Shotgun"/>, but only with a target.
    /// </summary>
    Cone,

    /// <summary>
    ///     As <see cref="Spiral"/>.
    /// </summary>
    Wave,

    /// <summary>
    ///     As <see cref="Cone"/>.
    /// </summary>
    TargetedBurst,

    /// <summary>
    ///     <see cref="NpcRangedAttackPatternComponent.Shots"/> at the target, each off by up to
    ///         <see cref="NpcRangedAttackPatternComponent.Spread"/> degrees either way.
    /// </summary>
    RapidFire,
}

/// <summary>
///     One ranged attack an NPC can make: what it fires, how it aims, and how often. Lives on an entity prototype in the
///         <c>NpcAttackPattern</c> category, read from there and never spawned. See
///         <see cref="NpcRangedAttackPatternHolderComponent"/>.
/// </summary>
[RegisterComponent]
[EntityCategory("NpcAttackPattern")]
public sealed partial class NpcRangedAttackPatternComponent : Component
{
    [DataField]
    public NpcRangedType AttackType = NpcRangedType.Single;

    [DataField(required: true)]
    public EntProtoId Projectile;

    /// <summary>
    ///     Projectiles per burst, for the patterns that use it.
    /// </summary>
    [DataField]
    public int Shots = 1;

    /// <summary>
    ///     How far a spiral or wave turns each burst.
    /// </summary>
    [DataField]
    public float DegreesPerShot = 15f;

    /// <summary>
    ///     How far apart, in degrees, a double spiral's two streams are.
    /// </summary>
    [DataField]
    public float RotationOffset;

    /// <summary>
    ///     How wide, in degrees, a fan of shots is; for rapid fire, how far off each shot may go either way.
    /// </summary>
    [DataField]
    public float Spread = 45f;

    [DataField]
    public float Speed = 2f;

    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>
    ///     A sprite flick on the NPC as the attack starts, warning of it.
    /// </summary>
    [DataField]
    public KsSpriteFlickData? TelegraphSpriteFlickData;

    /// <summary>
    ///     How long after its last burst before the attack can be made again.
    /// </summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Time between bursts.
    /// </summary>
    [DataField]
    public TimeSpan ShotDelay = TimeSpan.FromSeconds(0.1);

    [DataField]
    public int BurstCount = 1;
}
