using Robust.Shared.GameStates;

namespace Content.Shared._KS14.RemoteDrone;

/// <summary>
///     A remotely controlled drone.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class RemoteDroneComponent : Component
{
    /// <summary>
    ///     Entity with <see cref="RemoteDroneControllerComponent"/>
    ///         linked to this drone.
    /// </summary>
    [AutoNetworkedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? LinkedControllerUid = null;
}

/// <summary>
///     Raised when a remote drone <i>starts</i> being controlled by something,
///         on both the drone and controller, after all related internal logic is completed.
/// </summary>
[ByRefEvent]
public record struct RemoteDroneControlStartedEvent(Entity<RemoteDroneControllerComponent> ControllerEntity, EntityUid DroneUid);

/// <summary>
///     Raised when a remote drone <i>stops</i> being controlled by something,
///         on both the drone and controller, after all related internal logic is completed.
///
///     The drone may not exist when this is called. When this is raised, the drone's viewsubscriber
///         and <see cref="RemoteDroneControllerComponent.UserSession"/> have not yet been removed.
/// </summary>
[ByRefEvent]
public record struct RemoteDroneControlEndedEvent(Entity<RemoteDroneControllerComponent> ControllerEntity, EntityUid? DroneUid);

