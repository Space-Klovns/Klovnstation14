using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Ladders;

/// <summary>
///     A ladder: using it moves the user to the ladder it is linked to, on this map or another.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsLadderSystem))]
public sealed partial class KsLadderComponent : Component
{
    /// <summary>
    ///     The ladder at the other end. Two-way: that ladder's <see cref="Linked"/> points back here.
    /// </summary>
    /// <seealso cref="KsLadderSystem.Link"/>
    [DataField, AutoNetworkedField]
    public EntityUid? Linked;

    /// <summary>
    ///     How long climbing takes, as a DoAfter. Null climbs instantly.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? UseDelay;

    /// <summary>
    ///     Mapper link: at MapInit, an unlinked ladder links to the other unlinked ladder with the same key.
    ///     Kept after linking: a save keeps <see cref="Linked"/> only when both ends are in the same file, so a
    ///         ladder whose other end is on another map relinks by its key each time the maps are loaded.
    /// </summary>
    [DataField]
    public string? LinkKey;
}
