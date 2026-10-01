using System.Numerics;
using Content.Shared._KS14.NPC;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Perception;

public sealed partial class NpcPerceptionSystem
{
    /// <summary>
    ///     A target with stealth below this visibility, from -1 to 1, is not seen at all.
    /// </summary>
    private const float StealthVisibilityThreshold = 0.5f;

    /// <summary>
    ///     Whether the NPC can see the target now. Inside any container it cannot, ever. Otherwise it needs line of
    ///         sight, and - with light detection on - the target has to be conspicuous: close, lit, or moving fast.
    /// </summary>
    /// <remarks>
    ///     A target the NPC is already watching (<paramref name="watched"/>) stays seen in the dark for a while,
    ///         and for as long as it keeps moving: the NPC follows a silhouette it already has its eyes on. Without
    ///         that, someone walking through patchy light pops in and out of existence for it. Standing still in the
    ///         dark for <see cref="NpcPerceptionComponent.DarkTrackTime"/> loses it, and spotting a target in the
    ///         dark in the first place needs it to be conspicuous. A cloaked target is not seen at all.
    /// </remarks>
    private bool CanSee(Entity<NpcPerceptionComponent> entity,
        MapCoordinates observerMapCoordinates,
        EntityUid targetUid,
        float range,
        NpcContact? watched,
        TimeSpan now,
        out bool conspicuous,
        out Vector2 targetVelocity)
    {
        conspicuous = false;
        targetVelocity = Vector2.Zero;

        if (_containerSystem.IsEntityOrParentInContainer(targetUid))
            return false;

        // Cloaked: the same threshold as TargetIsVisibleCon. Only a target that has stealth pays for the check.
        if (_stealthQuery.TryComp(targetUid, out var stealthComponent) &&
            _stealthSystem.GetVisibility(targetUid, stealthComponent) < StealthVisibilityThreshold)
            return false;

        var targetMapCoordinates = _transformSystem.GetMapCoordinates(targetUid);
        if (targetMapCoordinates.MapId != observerMapCoordinates.MapId)
            return false;

        var distanceSquared = (targetMapCoordinates.Position - observerMapCoordinates.Position).LengthSquared();
        if (distanceSquared > range * range ||
            !InLineOfSight(observerMapCoordinates, targetMapCoordinates, range))
            return false;

        targetVelocity = _physicsSystem.GetMapLinearVelocity(targetUid);
        var speed = targetVelocity.Length();

        // Cheapest first: the light level is the only part that is not a field read.
        conspicuous = distanceSquared <= entity.Comp.ProximityRange * entity.Comp.ProximityRange ||
            speed >= entity.Comp.RevealSpeed ||
            _npcLightDetectionSystem.GetLightLevel(targetUid) >= entity.Comp.MinimumLightLevel;

        if (conspicuous)
            return true;

        return watched is { } watchedContact &&
            (now - watchedContact.LastConspicuous <= entity.Comp.DarkTrackTime || speed >= entity.Comp.TrackSpeed);
    }
}
