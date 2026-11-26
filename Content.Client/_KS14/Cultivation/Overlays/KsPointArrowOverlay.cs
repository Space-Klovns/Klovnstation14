using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Cultivation.Overlays;

/// <summary>
///     A gold arrow circling the player, pointing at where the server said, with how far it is; it pulses and fades out.
/// </summary>
public sealed partial class KsPointArrowOverlay : Overlay
{
    private const float Orbit = 70f;
    private const float FadeTime = 0.8f;
    private static readonly Color Gold = Color.FromHex("#ffd25a");

    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IResourceCache _resourceCache = default!;

    private readonly KsPointArrowSystem _arrowSystem;
    private readonly SharedTransformSystem _transformSystem;
    private readonly Font _font;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public KsPointArrowOverlay()
    {
        IoCManager.InjectDependencies(this);
        _arrowSystem = _entityManager.System<KsPointArrowSystem>();
        _transformSystem = _entityManager.System<SharedTransformSystem>();
        _font = new VectorFont(_resourceCache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 11);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var now = _gameTiming.RealTime;
        if (_arrowSystem.Target is not { } target || now >= _arrowSystem.Until || _playerManager.LocalEntity is not { } player)
            return;

        var here = _transformSystem.GetMapCoordinates(player);
        if (here.MapId != target.MapId)
            return;

        var distance = (target.Position - here.Position).Length();
        var hereScreen = _eyeManager.MapToScreen(here).Position;
        var thereScreen = _eyeManager.MapToScreen(target).Position;
        var dir = thereScreen - hereScreen;
        if (dir.LengthSquared() < 1f)
            return;

        dir = Vector2.Normalize(dir);
        var left = (float)(_arrowSystem.Until - now).TotalSeconds;
        var alpha = Math.Clamp(left / FadeTime, 0f, 1f);
        var pulse = 0.75f + 0.25f * MathF.Sin((float)(now - _arrowSystem.Since).TotalSeconds * 6f);
        var color = Gold.WithAlpha(alpha * pulse);
        var outline = Color.Black.WithAlpha(alpha * 0.7f);

        // The arrow: a head and a short shaft, a little way out from the player, bobbing toward the target.
        var bob = 6f * MathF.Sin((float)(now - _arrowSystem.Since).TotalSeconds * 4f);
        var tip = hereScreen + dir * (Orbit + 22f + bob);
        var baseCentre = hereScreen + dir * (Orbit + bob);
        var side = new Vector2(-dir.Y, dir.X);
        var head = new[] { tip, baseCentre + side * 13f, baseCentre - side * 13f };
        var headOutline = new[] { tip + dir * 2.5f, baseCentre + side * 16f - dir * 1.5f, baseCentre - side * 16f - dir * 1.5f };
        args.ScreenHandle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, headOutline, outline);
        args.ScreenHandle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, head, color);
        var shaftStart = hereScreen + dir * (Orbit - 18f + bob);
        var shaft = new[]
        {
            shaftStart + side * 4f, baseCentre + side * 4f, baseCentre - side * 4f,
            shaftStart + side * 4f, baseCentre - side * 4f, shaftStart - side * 4f,
        };
        args.ScreenHandle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, shaft, color);

        // Where, and how far.
        var text = Loc.GetString("ks-point-arrow-label", ("place", _arrowSystem.Label), ("distance", (int)MathF.Round(distance)));
        var textPos = hereScreen + dir * (Orbit + 44f) - new Vector2(40f, 8f);
        args.ScreenHandle.DrawString(_font, textPos + Vector2.One, text, outline);
        args.ScreenHandle.DrawString(_font, textPos, text, Gold.WithAlpha(alpha));
    }
}
