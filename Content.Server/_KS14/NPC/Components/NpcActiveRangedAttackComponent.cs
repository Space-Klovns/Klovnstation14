using Content.Server._KS14.NPC.Systems;

namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     On an NPC while one of its ranged attacks is firing, burst by burst; removed after the last. See
///         <see cref="NpcCombatRangedPatternSystem"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(NpcCombatRangedPatternSystem))]
public sealed partial class NpcActiveRangedAttackComponent : Component
{
    [ViewVariables]
    public NpcRangedAttackState State;

    /// <summary>
    ///     When the next burst fires.
    /// </summary>
    [AutoPausedField, ViewVariables]
    public TimeSpan NextBurstAt;
}

/// <summary>
///     Where an attack in progress has got to. Kept on <see cref="NpcActiveRangedAttackComponent"/>, and changed in
///         place through a <c>ref</c> to it.
/// </summary>
public record struct NpcRangedAttackState(string AttackId, NpcRangedAttackPatternComponent Pattern, EntityUid? TargetUid)
{
    public int BurstsFired;

    /// <summary>
    ///     Where a spiral or wave is pointing, in radians.
    /// </summary>
    public float Angle;
}
