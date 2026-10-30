namespace Content.Shared._KS14.Portal;

/// <summary>
///     Raised on an entity just after <see cref="KsPortalTravelSystem"/> carried it through a portal.
/// </summary>
/// <param name="Rotation">How far the entity, and its view of the world, was turned by the crossing.</param>
[ByRefEvent]
public readonly record struct KsPortalTravelledEvent(Angle Rotation);
