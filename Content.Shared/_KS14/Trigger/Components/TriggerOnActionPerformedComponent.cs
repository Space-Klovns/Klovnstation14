using Content.Shared.Actions;
using Content.Shared.Trigger.Components.Triggers;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Trigger.Components;

/// <summary>
///     When added to an action entity, triggers when the action is performed. User is passed.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class TriggerOnActionPerformedComponent : BaseTriggerOnXComponent
{
    [DataField, AutoNetworkedField]
    public bool UserIsTarget = false;
}


public interface IKsTriggerOnActionPerformedAction;
public sealed partial class KsTriggerOnActionPerformedBaseEvent : BaseActionEvent, IKsTriggerOnActionPerformedAction;

// These actions are always marked as handled when TriggerOnActionPerformedComponent is present
public sealed partial class KsTriggerOnActionPerformedWorldTargetActionEvent : WorldTargetActionEvent, IKsTriggerOnActionPerformedAction;
public sealed partial class KsTriggerOnActionPerformedInstantActionEvent : InstantActionEvent, IKsTriggerOnActionPerformedAction;
public sealed partial class KsTriggerOnActionPerformedEntityTargetActionEvent : EntityTargetActionEvent, IKsTriggerOnActionPerformedAction;
