using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Execution;

/// <summary>
///     Added to the thing doing the execution.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class KsActiveExecutionComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? VictimUid = null;

    [DataField, AutoNetworkedField]
    public TimeSpan NextPopupTime = TimeSpan.MinValue;
}
