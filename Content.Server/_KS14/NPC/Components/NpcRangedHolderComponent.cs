using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     Component that holds references to all NPC attacks
/// </summary>
[RegisterComponent]
public sealed partial class NpcRangedAttackPatternHolderComponent : Component
{
    [DataField]
    public Dictionary<string, EntProtoId> Attacks { get; set; } = new();

    [DataField]
    public Dictionary<string, float> Cooldowns { get; set; } = new();
}
