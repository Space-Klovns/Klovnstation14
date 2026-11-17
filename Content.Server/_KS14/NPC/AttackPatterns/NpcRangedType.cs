namespace Content.Server._KS14.NPC.AttackPatterns;

/// <summary>
///     How an attack's projectiles are aimed, burst by burst. See <see cref="NpcCombatRangedPatternSystem"/>.
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
