using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Cinematics;

/// <summary>
///     One shot of a cinematic. Animated states are stretched or squashed to fit <paramref name="Duration"/>.
/// </summary>
public readonly record struct KsCinematicShot(RSI.State State, TimeSpan Duration);

/// <summary>
///     Full-window control that plays a sequence of RSI states over a black background.
///     Add it as the last child of the window root so it covers the game and every bit of UI.
/// </summary>
public sealed partial class KsCinematicControl : Control
{
    [Dependency] private IGameTiming _timing = default!;

    private readonly KsCinematicShot[] _scenes;
    private readonly TimeSpan _startTime;
    private readonly TimeSpan _fadeIn;
    private readonly TimeSpan _fadeOut;

    public readonly TimeSpan Duration;

    public TimeSpan Elapsed => _timing.RealTime - _startTime;

    public bool Finished => Elapsed >= Duration;

    public KsCinematicControl(KsCinematicShot[] scenes, TimeSpan fadeIn, TimeSpan fadeOut)
    {
        IoCManager.InjectDependencies(this);

        _scenes = scenes;
        _fadeIn = fadeIn;
        _fadeOut = fadeOut;
        _startTime = _timing.RealTime;

        foreach (var scene in scenes)
        {
            Duration += scene.Duration;
        }

        // Swallow all mouse input so nothing underneath can be clicked.
        MouseFilter = MouseFilterMode.Stop;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var elapsed = Elapsed;
        var alpha = 1f;
        if (_fadeIn > TimeSpan.Zero && elapsed < _fadeIn)
            alpha = (float)(elapsed / _fadeIn);
        else if (_fadeOut > TimeSpan.Zero && Duration - elapsed < _fadeOut)
            alpha = (float)((Duration - elapsed) / _fadeOut);

        Modulate = Color.White.WithAlpha(Math.Clamp(alpha, 0f, 1f));
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(PixelSizeBox, Color.Black);

        if (GetCurrentTexture() is not { } texture)
            return;

        var textureSize = (Vector2)texture.Size;
        var scale = MathF.Min(PixelSize.X / textureSize.X, PixelSize.Y / textureSize.Y);
        var drawSize = textureSize * scale;
        handle.DrawTextureRect(texture, UIBox2.FromDimensions(((Vector2)PixelSize - drawSize) / 2f, drawSize));
    }

    private Texture? GetCurrentTexture()
    {
        if (_scenes.Length == 0)
            return null;

        var elapsed = Elapsed;
        foreach (var scene in _scenes)
        {
            if (elapsed < scene.Duration)
                return GetSceneFrame(scene, elapsed);

            elapsed -= scene.Duration;
        }

        // Hold the very last frame while fading out.
        var last = _scenes[^1];
        return last.State.GetFrame(RsiDirection.South, last.State.DelayCount - 1);
    }

    private static Texture GetSceneFrame(KsCinematicShot scene, TimeSpan elapsed)
    {
        var state = scene.State;
        if (!state.IsAnimated || state.AnimationLength <= 0f)
            return state.GetFrame(RsiDirection.South, 0);

        // Map scene time onto the state's own animation time.
        var animationTime = (float)(elapsed / scene.Duration) * state.AnimationLength;
        var frame = 0;
        while (frame < state.DelayCount - 1 && animationTime >= state.GetDelay(frame))
        {
            animationTime -= state.GetDelay(frame);
            frame++;
        }

        return state.GetFrame(RsiDirection.South, frame);
    }
}
