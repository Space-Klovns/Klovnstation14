using Content.Shared.Trigger.Components.Effects;

namespace Content.Server._KS14.Trigger.Components;

/// <summary>
///     When added to an action entity, triggers when the action is performed. User is passed.
/// </summary>
// KS14: dropped NetworkedComponent - this type only exists in Content.Server, so networking it shifted the
// alphabetically-sorted client/server NetID assignment for every later networked component (incl. TransformComponent),
// causing the client to remove the wrong component while applying entity state.
[RegisterComponent]
public sealed partial class SetHtnEnabledOnTriggerComponent : BaseXOnTriggerComponent
{
    [DataField]
    public bool Enabled = false;
}
