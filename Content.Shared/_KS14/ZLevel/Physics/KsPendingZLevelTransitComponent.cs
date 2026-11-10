using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ZLevel.Physics;

/// <summary>
///     Added to an entity that should be checked for whether it can start transiting between z-levels at its
///         current position, if it suddenly gains gravity.
///     Only ever present on entities that are not already transiting; see <see cref="KsZLevelTransitComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(KsZLevelPhysicsSystem))]
public sealed partial class KsPendingZLevelTransitComponent : Component;
