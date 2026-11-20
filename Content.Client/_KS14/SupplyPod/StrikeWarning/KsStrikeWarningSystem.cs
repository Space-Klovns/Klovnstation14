using System.Numerics;
using Content.Shared._KS14.SupplyPod.LandingZone;
using Content.Shared._KS14.SupplyPod.StrikeWarning;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Client._KS14.SupplyPod.StrikeWarning;

/// <summary>
///     Plays a strike warning over its countdown: the disc and crosshair are sized to the blast and pulse, faster and
///         faster towards the impact, while the chevrons close in on the rim. Unlike the supply pod indicator, nothing
///         turns and nothing shrinks to a point. Written every frame, as sprite layers can't be animated otherwise.
/// </summary>
public sealed partial class KsStrikeWarningSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // The remainder keeps the motion smooth between ticks instead of stepping at the tick rate.
        var curTime = _gameTiming.CurTime + _gameTiming.TickRemainder;
        var query = EntityQueryEnumerator<KsStrikeWarningComponent, SupplyPodLandingZoneIndicatorComponent, SpriteComponent>();

        while (query.MoveNext(out var uid, out var warningComponent, out var indicatorComponent, out var spriteComponent))
        {
            var elapsedSeconds = (float)Math.Max((curTime - indicatorComponent.StartTime).TotalSeconds, 0d);
            var durationSeconds = (float)indicatorComponent.Duration.TotalSeconds;
            var fraction = durationSeconds <= 0f ? 1f : Math.Clamp(elapsedSeconds / durationSeconds, 0f, 1f);

            var spriteEntity = new Entity<SpriteComponent?>(uid, spriteComponent);
            var areaScale = AreaScale(warningComponent);
            var chevronScale = areaScale * float.Lerp(warningComponent.ChevronStartScale, 1f, fraction);
            var alpha = PulseAlpha(warningComponent, Math.Min(elapsedSeconds, durationSeconds), durationSeconds);

            _spriteSystem.LayerSetScale(spriteEntity, KsStrikeWarningLayers.Area, new Vector2(areaScale, areaScale));
            _spriteSystem.LayerSetScale(spriteEntity, KsStrikeWarningLayers.Reticle, new Vector2(areaScale, areaScale));
            _spriteSystem.LayerSetScale(spriteEntity, KsStrikeWarningLayers.Chevrons, new Vector2(chevronScale, chevronScale));

            _spriteSystem.LayerSetColor(spriteEntity, KsStrikeWarningLayers.Area, Color.White.WithAlpha(alpha));
            _spriteSystem.LayerSetColor(spriteEntity, KsStrikeWarningLayers.Reticle, Color.White.WithAlpha(alpha));
        }
    }

    /// <summary>
    ///     The scale that puts the art's disc on the blast's rim.
    /// </summary>
    public static float AreaScale(KsStrikeWarningComponent warningComponent)
    {
        var radius = MathF.Max(warningComponent.Radius, warningComponent.MinimumRadius);
        return warningComponent.ArtRadius <= 0f ? 1f : radius / warningComponent.ArtRadius;
    }

    /// <summary>
    ///     Opacity of the pulsing layers. The rate rises linearly over the countdown, so the phase is its integral: a
    ///         chirp that never jumps back when the rate changes.
    /// </summary>
    private static float PulseAlpha(KsStrikeWarningComponent warningComponent, float elapsedSeconds, float durationSeconds)
    {
        var rateGain = durationSeconds <= 0f
            ? 0f
            : (warningComponent.PulseEndFrequency - warningComponent.PulseStartFrequency) / durationSeconds;
        var cycles = warningComponent.PulseStartFrequency * elapsedSeconds + 0.5f * rateGain * elapsedSeconds * elapsedSeconds;
        var wave = 0.5f + 0.5f * MathF.Cos(2f * MathF.PI * cycles);
        return float.Lerp(warningComponent.PulseMinimumAlpha, 1f, wave);
    }
}
