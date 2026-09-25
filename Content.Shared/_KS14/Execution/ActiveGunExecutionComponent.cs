using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Execution;

/// <summary>
///     Added to the thing doing the execution.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
[AutoGenerateComponentPause]
public sealed partial class ActiveGunExecutionComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? VictimUid = null;

    [DataField, AutoNetworkedField]
    [AutoPausedField]
    public TimeSpan NextPopupTime = TimeSpan.MinValue;
}
