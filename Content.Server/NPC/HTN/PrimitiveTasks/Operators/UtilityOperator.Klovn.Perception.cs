// KS14: added in this fork
using Content.Server._KS14.NPC.Perception;
using Robust.Shared.Map;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators;

public sealed partial class UtilityOperator
{
    [Dependency] private NpcPerceptionSystem _ksNpcPerceptionSystem = default!;
    [Dependency] private SharedTransformSystem _ksTransformSystem = default!;

    /// <summary>
    ///     Writes <see cref="KeyCoordinates"/> from what the owner believes about the target (see
    ///         <see cref="NpcPerceptionSystem"/>) instead of from the target itself: the target while it is in sight,
    ///         where it was last seen or called out once it is not, or the locker it is hiding in. Without this the
    ///         coordinates are attached to the target, and follow it wherever it goes - through walls included.
    /// </summary>
    [DataField]
    public bool BelievedCoordinates;

    /// <summary>
    ///     If set, the storage the target is believed to be hiding in is written here. The plan fails if there is none.
    /// </summary>
    [DataField]
    public string? ContainerKey;

    /// <summary>
    ///     If set, where the owner guesses the target has got to since it lost sight of it is written here
    ///         (see <see cref="NpcPerceptionSystem.TryGetPredictedCoordinates"/>). The plan fails if it cannot guess.
    /// </summary>
    [DataField]
    public string? PredictedCoordinatesKey;

    /// <summary>
    ///     If set, the world angle from the owner to that guess is written here, for <c>RotateToTargetOperator</c>.
    /// </summary>
    [DataField]
    public string? PredictedFacingKey;

    private bool TryAddPerceptionEffects(NPCBlackboard blackboard, EntityUid targetUid, Dictionary<string, object> effects)
    {
        if (!BelievedCoordinates && ContainerKey == null && PredictedCoordinatesKey == null && PredictedFacingKey == null)
            return true;

        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_ksNpcPerceptionSystem.TryGetBelievedCoordinates(ownerUid, targetUid, out var believedCoordinates, out _, out var containerUid))
            return false;

        if (BelievedCoordinates)
            effects[KeyCoordinates] = believedCoordinates;

        if (ContainerKey != null)
        {
            if (containerUid is not { } storageUid)
                return false;

            effects[ContainerKey] = storageUid;
        }

        if (PredictedCoordinatesKey == null && PredictedFacingKey == null)
            return true;

        if (!_ksNpcPerceptionSystem.TryGetPredictedCoordinates(ownerUid, targetUid, out var predictedCoordinates))
            return false;

        if (PredictedCoordinatesKey != null)
            effects[PredictedCoordinatesKey] = predictedCoordinates;

        if (PredictedFacingKey != null)
        {
            var ownerPosition = _ksTransformSystem.GetMapCoordinates(ownerUid).Position;
            var predictedPosition = _ksTransformSystem.ToMapCoordinates(predictedCoordinates).Position;
            effects[PredictedFacingKey] = (predictedPosition - ownerPosition).ToWorldAngle();
        }

        return true;
    }
}
