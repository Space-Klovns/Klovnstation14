namespace Content.Server._KS14.NPC.Components;

/// <summary>
/// Component that holds references to all NPC attacks
/// </summary>
[RegisterComponent]
public sealed partial class NPCRangedHolderComponent : Component
{
    [DataField("attacks")]
    public Dictionary<string, string> Attacks { get; set; } = new();

    [DataField("cooldowns")]
    public Dictionary<string, float> Cooldowns { get; set; } = new();
}
