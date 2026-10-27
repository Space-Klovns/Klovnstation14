using System.Numerics;
using Content.Shared._KS14.SupplyPod.LandingZone;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Client._KS14.SupplyPod.LandingZone;

/// <summary>
///     Plays the landing zone countdown. Sprite layers cannot be driven by the animation player
///         (it only reaches layer texture, state and colour), so this writes the layers each frame.
///         Both effects are linear, as in tgstation's <c>animate()</c>.
/// </summary>
public sealed partial class SupplyPodLandingZoneSystem : SharedSupplyPodLandingZoneSystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // The remainder keeps the motion smooth between ticks instead of stepping at the tick rate.
        var curTime = _gameTiming.CurTime + _gameTiming.TickRemainder;
        var eqe = EntityQueryEnumerator<SupplyPodLandingZoneIndicatorComponent, SpriteComponent>();

        while (eqe.MoveNext(out var uid, out var indicatorComponent, out var spriteComponent))
        {
            var fraction = indicatorComponent.Duration <= TimeSpan.Zero
                ? 1f
                : Math.Clamp((float)((curTime - indicatorComponent.StartTime).TotalSeconds / indicatorComponent.Duration.TotalSeconds), 0f, 1f);

            var spriteEntity = new Entity<SpriteComponent?>(uid, spriteComponent);

            _spriteSystem.LayerSetRotation(spriteEntity, SupplyPodLandingZoneLayers.Zone, indicatorComponent.ZoneTurn * fraction);

            var sliderScale = float.Lerp(indicatorComponent.SliderStartScale, indicatorComponent.SliderEndScale, fraction);
            _spriteSystem.LayerSetScale(spriteEntity, SupplyPodLandingZoneLayers.Slider, new Vector2(sliderScale, sliderScale));
        }
    }
}
