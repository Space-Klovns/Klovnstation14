using Content.Shared.Trigger.Components.Effects;
using Robust.Shared.GameStates;

namespace Content.Server._KS14.Trigger.Components;

/// <summary>
///     When added to an action entity, triggers when the action is performed. User is passed.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SetHtnEnabledOnTriggerComponent : BaseXOnTriggerComponent
{
    [DataField]
    public bool Enabled = false;
}
