namespace Content.Server._KS14.NPC.Components;

/// <summary>
/// Component that tracks anger modifier for NPCs based on damage taken.
/// Higher anger increases the chance of using attacks that have it set in their HTN.
/// </summary>
[RegisterComponent]
public sealed partial class NPCAngerModifierComponent : Component
{
    [DataField("angerModifier")]
    public float AngerModifier { get; set; } = 0f;

    [DataField("maxAnger")]
    public float MaxAnger { get; set; } = 20f;

    [DataField("damagePerAnger")]
    public float DamagePerAnger { get; set; } = 40f;
}
