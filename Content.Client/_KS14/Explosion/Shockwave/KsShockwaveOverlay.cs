using System.Numerics;
using Content.Shared._KS14.Explosion.Shockwave;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Explosion.Shockwave;

/*
    The original version of this source code was ported from
        https://github.com/RMC-14/RMC-14/ at commit 2066df33076c46e67bed4770d7c14ebf107c643b
*/

public sealed class KsShockwaveOverlay(ShaderInstance shader) : Overlay
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader = shader;

    private readonly List<TransientShockwaveDatum> _data = [];

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye == null)
            return false;

        var query = _entMan.EntityQueryEnumerator<KsShockwaveComponent, TransformComponent>();

        _data.Clear();
        while (query.MoveNext(out var uid, out var component, out var transformComponent))
        {
            if (transformComponent.MapID != args.MapId)
                continue;

            var worldPosition = _transformSystem.GetWorldPosition(uid);
            var tempCoords = args.Viewport.WorldToLocal(worldPosition);

            // normalized coords, 0 - 1 plane. This is pure hell, we subtract 1 because fragment calculates from the bottom and local goes from the top of the viewport
            // TODO fix this
            tempCoords.Y = 1 - tempCoords.Y / args.Viewport.Size.Y;
            tempCoords.X /= args.Viewport.Size.X;

            var datum = new TransientShockwaveDatum(component.StartTime, tempCoords, component.FalloffPower, component.Sharpness, component.Width);
            _data.Add(datum);
        }

        return _data.Count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var worldHandle = args.WorldHandle;
        worldHandle.UseShader(_shader);

        _shader?.SetParameter("renderScale", args.Viewport.RenderScale * args.Viewport.Eye!.Scale);
        _shader?.SetParameter("SCREEN_TEXTURE", ScreenTexture);

        foreach (var datum in _data)
        {
            _shader?.SetParameter("timeOffset", (float)(_gameTiming.CurTime - datum.StartTime).TotalSeconds);
            _shader?.SetParameter("position", datum.Position);
            _shader?.SetParameter("falloffPower", datum.FalloffPower);
            _shader?.SetParameter("sharpness", datum.Sharpness);
            _shader?.SetParameter("width", datum.Width);
            worldHandle.DrawRect(args.WorldBounds, Color.White);
        }

        worldHandle.UseShader(null);
    }

    private readonly record struct TransientShockwaveDatum(TimeSpan StartTime, Vector2 Position, float FalloffPower, float Sharpness, float Width);
}
