using Robust.Shared.Player;

namespace Content.Server._KS14.ZLevel;

/// <summary>
///     Used to add/remove a z-level loading entity for this player
///         depending on whether they're on a z-level or not.
/// </summary>
[RegisterComponent]
[Access(typeof(KsZLevelPvsSystem))]
public sealed partial class KsZLevelViewerComponent : Component
{
    /// <summary>
    ///     Should always point to a valid session as long as this component
    ///         is alive.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public ICommonSession Session;

    /// <summary>
    ///     Should be treated as undefined and not used, if <see cref="Active"/>
    ///         is false.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid ViewSubscriberUid;

    [ViewVariables(VVAccess.ReadOnly)]
    public bool Active;
}
