using Content.Shared.Trigger.Components.Effects;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Trigger.Components;

/// <summary>
///     Defaults the used gun to first thing in active hand.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StartGunExecutionOnTriggerComponent : BaseXOnTriggerComponent
{
    /// <summary>
    ///     How long the execution takes.
    /// </summary>
    [DataField(required: true)]
    [AutoNetworkedField]
    public TimeSpan Duration = TimeSpan.Zero;

    /// <summary>
    ///     If true, the target will automatically bolt or whatever the weapon.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public bool AutoHandle = false;
}
