using Robust.Shared.GameStates;

namespace Content.Shared._KS14.PredictedSpawning;

/// <summary>
///     When added on client, deletes the entity it was added to.
/// </summary>
[RegisterComponent, NetworkedComponent]
[UnsavedComponent]
public sealed partial class KsPredictedSpawnComponent : Component;
