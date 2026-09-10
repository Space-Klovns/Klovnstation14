using Content.Shared._KS14.Trail;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Client._KS14.Trail;

/// <summary>
///     Animates a trail's blur and colour as it dies off. Purely clientside — the server only
///         ever tells us when the fade started.
/// </summary>
public sealed partial class KsTrailFadeSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var curTime = _gameTiming.CurTime;
        var eqe = EntityQueryEnumerator<KsTrailFadeComponent, KsTrailComponent>();

        while (eqe.MoveNext(out var fadeComponent, out var trailComponent))
        {
            if (fadeComponent.StartTime == TimeSpan.MaxValue)
                continue;

            if (!fadeComponent.Captured)
            {
                fadeComponent.InitialAlpha = trailComponent.Color.A;
                fadeComponent.Captured = true;
            }

            var elapsed = curTime - fadeComponent.StartTime;
            if (elapsed < TimeSpan.Zero)
                continue;

            var blurFraction = Ease(GetFraction(elapsed, fadeComponent.BlurDuration), fadeComponent.BlurEasing);
            var alphaFraction = Ease(GetFraction(elapsed, fadeComponent.AlphaDuration), fadeComponent.AlphaEasing);

            trailComponent.Blur = float.Lerp(fadeComponent.StartBlur, fadeComponent.EndBlur, blurFraction);
            trailComponent.Color = trailComponent.Color.WithAlpha(
                float.Lerp(fadeComponent.InitialAlpha, fadeComponent.TargetAlpha, alphaFraction)
            );
        }
    }

    private static float Ease(float fraction, KsTrailEasing easing) => easing switch
    {
        KsTrailEasing.CubicIn => fraction * fraction * fraction,
        KsTrailEasing.CubicOut => 1f - MathF.Pow(1f - fraction, 3f),
        _ => fraction,
    };

    private static float GetFraction(TimeSpan elapsed, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return 1f;

        return Math.Clamp((float)(elapsed.TotalSeconds / duration.TotalSeconds), 0f, 1f);
    }
}
