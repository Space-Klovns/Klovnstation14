namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     Lets an NPC see in the dark, on the NPC or on something it wears: night vision goggles, say. Answers
///         <see cref="NpcGetDarkVisionEvent"/>. It has no switch and needs no power, as an NPC never has to think of
///         turning its goggles on.
/// </summary>
[RegisterComponent]
public sealed partial class NpcDarkVisionComponent : Component
{
    /// <summary>
    ///     How much of the dark this sees through, from 0, not at all, to 1, as if everything were fully lit.
    /// </summary>
    [DataField]
    public float Strength = 1f;
}
