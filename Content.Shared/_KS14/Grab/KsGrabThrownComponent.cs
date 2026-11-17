using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Grab;

/// <summary>
///     Someone thrown out of a grab, still in flight. Whatever they slam into first knocks them down, and knocks
///         over whoever they hit, as a shove would. Removed when they land.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsGrabSystem))]
public sealed partial class KsGrabThrownComponent : Component
{
    /// <summary>
    ///     Who threw them.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid Thrower;
}
