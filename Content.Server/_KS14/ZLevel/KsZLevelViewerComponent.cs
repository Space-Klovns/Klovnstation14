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

    /// <summary>
    ///     The same again for the z-level directly above, which is only ever sent so that light can fall from
    ///         it - nothing up there is drawn.
    /// </summary>
    /// <remarks>
    ///     Kept separate rather than folded into a list because the two have different lifetimes: the one
    ///         below follows the player around the whole round, and this one appears and disappears with a
    ///         cvar.
    /// </remarks>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid AboveViewSubscriberUid;

    [ViewVariables(VVAccess.ReadOnly)]
    public bool AboveActive;
}
