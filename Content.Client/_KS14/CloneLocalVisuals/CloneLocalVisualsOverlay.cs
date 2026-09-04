using Content.Shared._KS14.CloneLocalVisuals;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;

namespace Content.Client._KS14.CloneLocalVisuals;

public sealed partial class CloneLocalVisualsOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private EntityManager _entityManager = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;

    public CloneLocalVisualsOverlay()
    {
        ZIndex = (int)Shared.DrawDepth.DrawDepth.OverMobs;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_playerManager.LocalEntity is not { })
            return false;

        foreach (var _ in _entityManager.EntityQuery<CloneLocalVisualsComponent>())
            return true;

        return false;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var uid = _playerManager.LocalEntity!.Value;
        if (!_entityManager.TryGetComponent<SpriteComponent>(uid, out var spriteComponent))
            return;

        var eyeRotation = args.Viewport.Eye?.Rotation ?? default;

        var eqe = _entityManager.EntityQueryEnumerator<CloneLocalVisualsComponent, TransformComponent>();
        while (eqe.MoveNext(out _, out _, out var transformComponent))
        {
            var (worldPosition, worldRotation) = _transformSystem.GetWorldPositionRotation(transformComponent);
            _spriteSystem.RenderSprite((uid, spriteComponent), args.WorldHandle, eyeRotation, worldRotation, worldPosition);
        }
    }
}
