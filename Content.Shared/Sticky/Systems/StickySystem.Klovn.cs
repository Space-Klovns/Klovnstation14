using Content.Shared.Sticky.Components;

namespace Content.Shared.Sticky.Systems;

public sealed partial class StickySystem
{
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    /// <summary>
    ///     Turns a freshly-stuck entity to face the user, snapped to a cardinal direction of whatever it
    ///         got stuck to, then pushes it out by its <see cref="StickyComponent.StuckOffset"/>.
    /// </summary>
    private void RotateAndMove(Entity<StickyComponent> stuckEntity, EntityUid userUid)
    {
        var stuckTransformComponent = Transform(stuckEntity);
        var parentUid = stuckTransformComponent.ParentUid;

        if (!parentUid.IsValid())
            return;

        var worldDelta = _transformSystem.GetWorldPosition(userUid) - _transformSystem.GetWorldPosition(stuckTransformComponent);

        // Sticking something to whatever you are already standing on leaves no direction to face.
        if (worldDelta.LengthSquared() <= float.Epsilon)
            return;

        // ToWorldAngle, not ToAngle: entity rotation treats zero as south, so the plain trigonometric
        // angle would leave this a quarter turn off and mirrored.
        var worldRotation = worldDelta.ToWorldAngle();

        // SetLocalRotation is relative to the parent, so take the parent's rotation back out before
        // snapping. Rounding in the parent's frame is also what lines the entity up with the wall it is
        // stuck to rather than with world north, which is what made rotated grids come out skewed.
        var localRotation = (worldRotation - _transformSystem.GetWorldRotation(parentUid)).RoundToCardinalAngle();

        _transformSystem.SetLocalRotation(stuckEntity.Owner, localRotation, xform: stuckTransformComponent);
        _transformSystem.SetLocalPosition(stuckEntity.Owner, localRotation.RotateVec(stuckEntity.Comp.StuckOffset), xform: stuckTransformComponent);
    }
}
