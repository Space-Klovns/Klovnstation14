using Content.Server._KS14.NPC.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     The ranged attacks an NPC can make, by the id HTN asks for them by, and when each comes off cooldown. See
///         <see cref="NpcCombatRangedPatternSystem"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(NpcCombatRangedPatternSystem))]
public sealed partial class NpcRangedAttackPatternHolderComponent : Component
{
    /// <summary>
    ///     Attack id to the prototype holding its <see cref="NpcRangedAttackPatternComponent"/>.
    /// </summary>
    [DataField]
    public Dictionary<string, EntProtoId> Attacks = new();

    /// <summary>
    ///     When each attack that has been made can be made again, by attack id.
    /// </summary>
    [AutoPausedField, ViewVariables]
    public Dictionary<string, TimeSpan> CooldownEnds = new();
}
