using Robust.Shared.GameStates;
using Robust.Shared.Timing;

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

    /// <summary>
    ///     The tick the server spawned this copy on, which is the tick of the user's input, so the same tick their
    ///         client spawned its own copy on. It tells that client which of its copies this one is. Only ever set
    ///         alongside <see cref="PredictingUser"/>.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public GameTick SpawnTick;
}
