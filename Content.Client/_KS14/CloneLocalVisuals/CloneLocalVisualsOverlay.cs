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
        var referenceUid = _playerManager.LocalEntity!.Value;
        if (!_entityManager.TryGetComponent<SpriteComponent>(referenceUid, out var referenceSpriteComponent))
            return;

        var eyeRotation = args.Viewport.Eye?.Rotation ?? default;

        var (oldOffset, oldRotation, oldColor) = (referenceSpriteComponent.Offset, referenceSpriteComponent.Rotation, referenceSpriteComponent.Color);
        var eqe = _entityManager.EntityQueryEnumerator<CloneLocalVisualsComponent, TransformComponent, SpriteComponent>();

        while (eqe.MoveNext(out _, out _, out var transformComponent, out var spriteComponent))
        {
            _spriteSystem.SetOffset((referenceUid, referenceSpriteComponent), spriteComponent.Offset);
            _spriteSystem.SetRotation((referenceUid, referenceSpriteComponent), spriteComponent.Rotation);
            _spriteSystem.SetColor((referenceUid, referenceSpriteComponent), spriteComponent.Color);

            var (worldPosition, worldRotation) = _transformSystem.GetWorldPositionRotation(transformComponent);
            _spriteSystem.RenderSprite((referenceUid, referenceSpriteComponent), args.WorldHandle, eyeRotation, worldRotation, worldPosition);
        }

        _spriteSystem.SetOffset((referenceUid, referenceSpriteComponent), oldOffset);
        _spriteSystem.SetRotation((referenceUid, referenceSpriteComponent), oldRotation);
        _spriteSystem.SetColor((referenceUid, referenceSpriteComponent), oldColor);
    }
}
