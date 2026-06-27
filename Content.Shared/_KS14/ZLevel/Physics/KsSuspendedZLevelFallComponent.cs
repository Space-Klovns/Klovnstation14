using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ZLevel.Physics;

/// <summary>
///     Added to an entity that should be checked for whether it can fall at its current position,
///         if it suddenly gains gravity.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(KsZLevelSystem))]
public sealed partial class KsSuspendedZLevelFallComponent : Component;
