using Content.Server.Explosion.EntitySystems;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Content.Shared.Explosion.Components;
using Content.Shared.Trigger.Components.Effects;
using Robust.Server.GameObjects;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     1 if the owner is near enough for something about to go off to reach it, 0 if not. A distance curve cannot say
///         this, as it only ranks: anything inside the query's reach scores above nothing, which sent NPCs running from
///         a grenade 20 tiles away.
/// </summary>
public sealed partial class TargetDangerRangeCon : UtilityConsideration
{
    [Dependency] private ExplosionSystem _explosionSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<ExplosiveComponent> _explosiveQuery = default!;
    [Dependency] private EntityQuery<FlashOnTriggerComponent> _flashOnTriggerQuery = default!;

    /// <summary>
    ///     How far outside the blast still counts, in tiles, to leave room for it having time to get clear.
    /// </summary>
    [DataField]
    public float Margin = 1f;

    /// <summary>
    ///     The reach, in tiles, of anything that doesn't say what it does: a bomb carried in a payload, say.
    /// </summary>
    [DataField]
    public float DefaultRange = 5f;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        var ownerCoordinates = _transformSystem.GetMapCoordinates(ownerUid);
        var targetCoordinates = _transformSystem.GetMapCoordinates(targetUid);

        if (ownerCoordinates.MapId != targetCoordinates.MapId)
            return 0f;

        var range = GetRange(targetUid) + Margin;
        return (ownerCoordinates.Position - targetCoordinates.Position).LengthSquared() <= range * range ? 1f : 0f;
    }

    private float GetRange(EntityUid targetUid)
    {
        if (_explosiveQuery.TryComp(targetUid, out var explosiveComponent))
        {
            return _explosionSystem.IntensityToRadius(
                explosiveComponent.TotalIntensity,
                explosiveComponent.IntensitySlope,
                explosiveComponent.MaxIntensity);
        }

        return _flashOnTriggerQuery.TryComp(targetUid, out var flashOnTriggerComponent)
            ? flashOnTriggerComponent.Range
            : DefaultRange;
    }
}
