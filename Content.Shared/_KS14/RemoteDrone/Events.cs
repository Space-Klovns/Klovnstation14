namespace Content.Shared._KS14.RemoteDrone;

/// <summary>
///     Raised when a remote drone is <i>linked to</i> its controller,
///         on both the drone and controller, after all related internal logic is completed.
/// </summary>
[ByRefEvent]
public record struct RemoteDroneLinkedEvent(Entity<RemoteDroneControllerComponent> ControllerEntity, EntityUid DroneUid);

/// <summary>
///     Raised when a remote drone is <i>unlinked from</i> its controller,
///         on both the drone and controller, after all related internal logic is completed.
/// </summary>
[ByRefEvent]
public record struct RemoteDroneUnlinkedEvent(Entity<RemoteDroneControllerComponent> ControllerEntity, EntityUid DroneUid);

/// <summary>
///     Raised when a remote drone <i>starts</i> being controlled by something,
///         on both the drone and controller.
/// </summary>
[ByRefEvent]
public record struct RemoteDroneControlStartedEvent(Entity<RemoteDroneControllerComponent> ControllerEntity, EntityUid DroneUid);

/// <summary>
///     Raised when a remote drone <i>stops</i> being controlled by something,
///         on both the drone and controller.
///
///     The drone may not exist when this is called. When this is raised, the drone's viewsubscriber
///         and <see cref="RemoteDroneControllerComponent.UserSession"/> have not yet been removed.
/// </summary>
[ByRefEvent]
public record struct RemoteDroneControlEndedEvent(Entity<RemoteDroneControllerComponent> ControllerEntity, EntityUid? DroneUid);

