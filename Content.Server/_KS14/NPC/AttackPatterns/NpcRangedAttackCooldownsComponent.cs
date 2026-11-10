using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.AttackPatterns;

/// <summary>
///     When each ranged attack an NPC has made comes off cooldown, and when it may start another attack at all. Added by
///         <see cref="NpcCombatRangedPatternSystem"/> the first time the NPC starts one, unless the prototype already
///         has it to set the gap between attacks.
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

    /// <summary>
    ///     The shortest pause after any attack's last burst before the next attack of any kind can start.
    /// </summary>
    [DataField]
    public TimeSpan MinAttackGap = TimeSpan.Zero;

    /// <summary>
    ///     The longest such pause. Each gap is picked at random between this and <see cref="MinAttackGap"/>.
    /// </summary>
    [DataField]
    public TimeSpan MaxAttackGap = TimeSpan.Zero;

    /// <summary>
    ///     When the gap after the last attack is over.
    /// </summary>
    [AutoPausedField, ViewVariables]
    public TimeSpan NextAttackAllowedAt;
}
