using Content.Server._KS14.NPC.Grenades;
using Content.Server.Explosion.EntitySystems;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Content.Shared.Explosion.Components;
using Content.Shared.Flash;
using Content.Shared.Trigger.Components.Effects;
using Robust.Server.GameObjects;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     1 if the owner is near enough for something about to go off to reach it, 0 if not. A distance curve cannot say
///         this, as it only ranks: anything inside the query's reach scores above nothing, which sent NPCs running from
///         a grenade 20 tiles away. A grenade an NPC threw counts from where it is coming down until it has: it starts out
///         at the thrower's feet, and the squad would otherwise run from its own throw. A flash is nothing to run from for
///         an owner it would do nothing to, however close.
/// </summary>
public sealed partial class TargetDangerRangeCon : UtilityConsideration
{
    [Dependency] private ExplosionSystem _explosionSystem = default!;
    [Dependency] private NpcGrenadeSystem _npcGrenadeSystem = default!;
    [Dependency] private SharedFlashSystem _flashSystem = default!;
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
        var targetCoordinates = _npcGrenadeSystem.TryGetLanding(targetUid, out var landing)
            ? _transformSystem.ToMapCoordinates(landing)
            : _transformSystem.GetMapCoordinates(targetUid);

        if (ownerCoordinates.MapId != targetCoordinates.MapId)
            return 0f;

        var range = GetRange(targetUid) + Margin;
        if ((ownerCoordinates.Position - targetCoordinates.Position).LengthSquared() > range * range)
            return 0f;

        return IsFlashProof(ownerUid, targetUid) ? 0f : 1f;
    }

    /// <summary>
    ///     How far <paramref name="targetUid"/> reaches.
    /// </summary>
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

    /// <summary>
    ///     Whether <paramref name="targetUid"/> is a flash that could not reach <paramref name="ownerUid"/>.
    /// </summary>
    private bool IsFlashProof(EntityUid ownerUid, EntityUid targetUid)
    {
        if (_explosiveQuery.HasComp(targetUid) || !_flashOnTriggerQuery.HasComp(targetUid))
            return false;

        return !_flashSystem.CanFlash(ownerUid);
    }
}
