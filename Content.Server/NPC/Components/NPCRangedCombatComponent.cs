using Content.Server.NPC.Systems;
using Robust.Shared.Audio;

namespace Content.Server.NPC.Components;

/// <summary>
/// Added to an NPC doing ranged combat.
/// </summary>
[RegisterComponent]
public sealed partial class NPCRangedCombatComponent : Component
{
    [ViewVariables]
    public EntityUid Target;

    [ViewVariables]
    public CombatStatus Status = CombatStatus.Normal;

    // Most of the below is to deal with turrets.

    /// <summary>
    /// If null it will instantly turn.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)] public Angle? RotationSpeed;

    /// <summary>
    /// Maximum distance, between our rotation and the target's, to consider shooting it.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public Angle AccuracyThreshold = Angle.FromDegrees(30);

    /// <summary>
    /// How long until the last line of sight check.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float LOSAccumulator = 0f;

    /// <summary>
    ///  Is the target still considered in LOS since the last check.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool TargetInLOS = false;

    /// <summary>
    /// If true, only opaque objects will block line of sight.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    // ReSharper disable once InconsistentNaming
    public bool UseOpaqueForLOSChecks = false;

    /// <summary>
    /// Delay after target is in LOS before we start shooting.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float ShootDelay = 0.2f;

    [ViewVariables(VVAccess.ReadWrite)]
    public float ShootAccumulator;

    /// <summary>
    /// Sound to play if the target enters line of sight.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier? SoundTargetInLOS;

    // KS14 start
    /// <summary>
    ///     Tells seeing the target from being able to hit it, for turrets. <see cref="TargetInLOS"/> is then sight: what
    ///         blocks light (the Opaque layer) and no mob, unless <see cref="KsStandingMobsBlockSight"/>. In sight, it aims and
    ///         tracks, and holds fire (<see cref="CombatStatus.KsShotBlocked"/>, and the task carries on) while a round would
    ///         not get to the target (<see cref="KsTargetInLineOfFire"/>) or would hit a friend. Set from
    ///         <c>GunOperator.KsHoldFireWhenBlocked</c>.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool KsHoldFireWhenBlocked;

    /// <summary>
    ///     With <see cref="KsHoldFireWhenBlocked"/>, whether a mob standing between hides the target. One lying down never
    ///         does. From the blackboard key <c>KsStandingMobsBlockSight</c>.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool KsStandingMobsBlockSight;

    /// <summary>
    ///     With <see cref="KsHoldFireWhenBlocked"/>: whether, at the last check, a round from the gun would get to the target
    ///         by what it collides with, glass stopping a bullet and not a laser. Mobs do not count; friends are for the fire
    ///         lanes. <c>NpcLineOfFireSystem.CanRoundReach</c>, which a turret also picks its target by
    ///         (<c>KsTargetInLineOfFireCon</c>).
    /// </summary>
    [ViewVariables]
    public bool KsTargetInLineOfFire;
    // KS14 end
}
