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
    ///     The entities loading the z-levels below for this player, one a level, the nearest level first. As many as
    ///         there are levels below, up to <see cref="Content.Shared._KS14.CCVar.KsCCVars.ZLevelPvsLevelsBelow"/>.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public List<EntityUid> BelowViewSubscriberUids = [];

    /// <summary>
    ///     The same again for the z-level directly above, which is only ever sent so that light can fall from
    ///         it - nothing up there is drawn.
    /// </summary>
    /// <remarks>
    ///     Kept separate rather than folded into the list because the two have different lifetimes: the ones
    ///         below follow the player around the whole round, and this one appears and disappears with a
    ///         cvar.
    /// </remarks>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid AboveViewSubscriberUid;

    [ViewVariables(VVAccess.ReadOnly)]
    public bool AboveActive;
}
