using Content.Shared.Trigger.Components.Effects;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Trigger.Components;

/// <summary>
///     Defaults the used weapon to the first thing in active hand. Checks
///         all hands.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KsStartExecutionOnTriggerComponent : BaseXOnTriggerComponent
{
    /// <summary>
    ///     If applicable, this is how long the execution takes.
    ///         Only works for gun executions at the moment.
    /// </summary>
    [DataField(required: true)]
    [AutoNetworkedField]
    public TimeSpan Duration = TimeSpan.Zero;

    /// <summary>
    ///     If true, the target will automatically bolt or whatever to the weapon.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public bool AutoHandle = false;
}
