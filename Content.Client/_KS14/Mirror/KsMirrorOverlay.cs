using System.Numerics;
using Content.Shared._KS14.Mirror;
using Content.Shared.Hands.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;

namespace Content.Client._KS14.Mirror;

public sealed class KsMirrorOverlay(ShaderInstance shader) : Overlay
{
    [Dependency] private readonly EntityManager _entityManager = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly MapSystem _mapSystem = default!;
    [Dependency] private readonly SpriteSystem _spriteSystem = default!;

    [Dependency] private readonly EntityQuery<MapGridComponent> _gridQuery = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader = shader;

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture is not { })
            return;

        var handle = args.WorldHandle;

        var eqe = _entityManager.EntityQueryEnumerator<KsMirrorReflectableComponent, TransformComponent>();
        while (eqe.MoveNext(out var uid, out var component, out var transformComponent))
        {
            var matrix = _transformSystem.GetWorldMatrix(transformComponent);
            handle.SetTransform(matrix);

            handle.
        }

        handle.SetTransform(Matrix3x2.Identity);

        handle.UseShader(_shader);
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
