using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Robust.Server.GameObjects;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     Nearer is better, at any distance: <c>1 / (1 + distance / scale)</c>, which halves at <see cref="Scale"/> tiles
///         and never reaches 0. <c>TargetDistanceCon</c> goes by the owner's vision and scores everything past it 0,
///         which drops it; this is for targets found well beyond sight, as <c>KsScenarioObjectiveQuery</c> finds them.
///         0 only for a target on another map.
/// </summary>
public sealed partial class KsTargetDistanceUnboundedCon : UtilityConsideration
{
    [Dependency] private TransformSystem _transformSystem = default!;

    /// <summary>
    ///     The distance, in tiles, that scores a half.
    /// </summary>
    [DataField]
    public float Scale = 10f;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        var ownerCoordinates = _transformSystem.GetMapCoordinates(ownerUid);
        var targetCoordinates = _transformSystem.GetMapCoordinates(targetUid);
        if (ownerCoordinates.MapId != targetCoordinates.MapId)
            return 0f;

        var distance = (targetCoordinates.Position - ownerCoordinates.Position).Length();
        return 1f / (1f + distance / MathF.Max(Scale, 0.01f));
    }
}
