using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Actions;

/// <summary>
///     A targeted action that, while <see cref="Skip"/> is set, goes off at once on the user's own spot instead of
///         asking them to aim. For an action that only needs aiming some of the time, like Returning Iron (aimed when
///         throwing, instant when binding or recalling).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KsSkipTargetingComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Skip;
}
