using Robust.Shared.GameStates;

namespace Content.Shared._KS14.PredictedSpawning;

/// <summary>
///     Marks an entity spawned through <see cref="KsSharedPredictedSpawnSystem"/>, whose physics the client predicts.
///
///     With <see cref="PredictingUser"/> set, the entity is one copy of a user-owned spawn: the user's own client
///         spawned and keeps a copy of its own, so on that client this, the server's copy, is hidden and inert. Every
///         other client shows it as usual.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
[Access(typeof(KsSharedPredictedSpawnSystem))]
[UnsavedComponent]
public sealed partial class KsPredictedSpawnComponent : Component
{
    /// <summary>
    ///     The entity whose client predicted this spawn and keeps its own copy of it, if any. Only ever set on the
    ///         server's copy.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? PredictingUser;
}
