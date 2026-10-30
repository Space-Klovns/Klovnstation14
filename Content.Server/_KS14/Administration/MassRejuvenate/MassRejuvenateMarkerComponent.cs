namespace Content.Server._KS14.Administration.MassRejuvenate;

/// <summary>
///     A mapper's marker that fully heals everything around it once, then deletes itself.
/// </summary>
/// <remarks>
///     Fires on map init, so it acts when the map it is on is loaded - not when the entity happens to be
///         constructed. It is a one-shot: once it has fired, the marker is gone.
/// </remarks>
[RegisterComponent]
[Access(typeof(MassRejuvenateSystem))]
public sealed partial class MassRejuvenateMarkerComponent : Component
{
    /// <summary>
    ///     How far, in tiles, the heal reaches from the marker.
    /// </summary>
    [DataField]
    public float Radius = 2f;

    /// <summary>
    ///     Whether only player-controlled entities are healed.
    /// </summary>
    /// <remarks>
    ///     False heals every damageable thing in range, mobs and NPCs included. Note that a player has
    ///         to actually be attached for an entity to count as player-controlled, so a marker that
    ///         fires before players have spawned in will find nobody.
    /// </remarks>
    [DataField]
    public bool PlayerControlledOnly = true;
}
