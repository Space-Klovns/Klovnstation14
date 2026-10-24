using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.AttackPatterns;

/// <summary>
///     When each ranged attack an NPC has made comes off cooldown. Added by <see cref="NpcCombatRangedPatternSystem"/>
///         the first time the NPC finishes one.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(NpcCombatRangedPatternSystem))]
public sealed partial class NpcRangedAttackCooldownsComponent : Component
{
    /// <summary>
    ///     When each attack, by its prototype, can be made again.
    /// </summary>
    [AutoPausedField, ViewVariables]
    public Dictionary<EntProtoId<NpcRangedAttackPatternComponent>, TimeSpan> CooldownEnds = new();
}
