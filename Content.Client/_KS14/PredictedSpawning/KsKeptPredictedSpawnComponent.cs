using System.Numerics;
using Content.Shared._KS14.PredictedSpawning;

namespace Content.Client._KS14.PredictedSpawning;

/// <summary>
///     This client's own copy of a user-mode spawn (see <see cref="KsSharedPredictedSpawnSystem"/>), kept for its
///         whole life rather than deleted on the next state reset. Client-only, on a client-only entity.
/// </summary>
[RegisterComponent, Access(typeof(KsPredictedSpawnSystem))]
public sealed partial class KsKeptPredictedSpawnComponent : Component
{
    /// <summary>
    ///     Whether <see cref="SimulatedPosition"/> and the fields beside it hold a step's result yet.
    /// </summary>
    [ViewVariables]
    public bool HasSimulatedTransform;

    /// <summary>
    ///     Where the last first-time-predicted physics step left the entity, before the transform lerp wrote its
    ///         interpolated, rendered position over it.
    /// </summary>
    [ViewVariables]
    public Vector2 SimulatedPosition;

    /// <inheritdoc cref="SimulatedPosition"/>
    [ViewVariables]
    public Angle SimulatedRotation;

    /// <summary>
    ///     The parent <see cref="SimulatedPosition"/> is relative to.
    /// </summary>
    [ViewVariables]
    public EntityUid SimulatedParent;

    /// <summary>
    ///     Where that step's lerp starts from. The lerp only ever puts the entity between this and
    ///         <see cref="SimulatedPosition"/>, so anywhere else means something else moved it.
    /// </summary>
    [ViewVariables]
    public Vector2 LerpOrigin;

    /// <summary>
    ///     Whether a deletion asked for while a state was being applied is waiting for the next tick.
    /// </summary>
    /// <seealso cref="KsPredictedSpawnSystem.PredictedQueueDelDeferrable"/>
    [ViewVariables]
    public bool DeletionDeferred;
}
