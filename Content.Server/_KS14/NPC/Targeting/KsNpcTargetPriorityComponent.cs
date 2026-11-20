namespace Content.Server._KS14.NPC.Targeting;

/// <summary>
///     How much NPCs that weigh target priority (<see cref="KsTargetPriorityCon"/>) prefer this entity as a target,
///         0 to 1. A target without it scores the consideration's own default.
/// </summary>
[RegisterComponent]
public sealed partial class KsNpcTargetPriorityComponent : Component
{
    [DataField]
    public float Priority = 1f;
}
