using Content.Shared.Sticky.Components;

namespace Content.Shared.Sticky.Systems;

public sealed partial class StickySystem
{
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    private void RotateAndMove(Entity<StickyComponent> stuckEntity, EntityUid userUid)
    {
        var stuckTransformComponent = Transform(stuckEntity);
        var userTransformComponent = Transform(userUid);

        _transformSystem.SetLocalRotation(
            stuckEntity.Owner,
            -(_transformSystem.GetWorldPosition(userTransformComponent) - _transformSystem.GetWorldPosition(stuckTransformComponent)).ToAngle().RoundToCardinalAngle(),
            xform: stuckTransformComponent
        );

        _transformSystem.SetLocalPosition(stuckEntity.Owner, stuckTransformComponent.LocalRotation.RotateVec(stuckEntity.Comp.StuckOffset), xform: stuckTransformComponent);
    }
}
