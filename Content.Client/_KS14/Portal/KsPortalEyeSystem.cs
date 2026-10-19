using Content.Client.Eye;
using Content.Shared._KS14.Portal;

namespace Content.Client._KS14.Portal;

/// <summary>
///     Keeps the local player's eye rotation lerp from sweeping when a portal crossing has just turned the
///         view: the eye's rotation follows the mover's parent angle, which the crossing has already turned
///         by the same amount in the opposite direction, so the lerp's start has to be turned with it.
/// </summary>
public sealed partial class KsPortalEyeSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnPortalTravelled(Entity<LerpingEyeComponent> entity, ref KsPortalTravelledEvent args)
    {
        // The eye turns by minus what the mover's parent angle does.
        entity.Comp.LastRotation -= args.Rotation;
        entity.Comp.TargetRotation -= args.Rotation;
    }
}
