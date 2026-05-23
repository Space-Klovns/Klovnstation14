using Content.Shared.Trigger.Components.Triggers;

namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     Disturbs nearby NPCs upon trigger.
/// </summary>
[RegisterComponent]
public sealed partial class NpcDisturbOnTriggerComponent : BaseTriggerOnXComponent
{
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Radius = 3f;

    [DataField]
    public bool TargetUser = false;
}
