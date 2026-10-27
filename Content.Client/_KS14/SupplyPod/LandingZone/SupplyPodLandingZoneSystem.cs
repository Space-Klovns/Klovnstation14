using System.Numerics;
using Content.Shared._KS14.SupplyPod.LandingZone;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Client._KS14.SupplyPod.LandingZone;

/// <summary>
///     Plays the landing zone countdown. Sprite layers cannot be driven by the animation player
///         (it only reaches layer texture, state and colour), so this writes the layers each frame.
///         Both effects are linear, as in tgstation's <c>animate()</c>.
/// </summary>
/// <remarks>
///     Both layers are drawn with <c>KsPixelSnap</c>, so as the slider shrinks and the zone turns their
///         texels never get smaller than a world pixel, as in BYOND. The slider is resized in whole
///         world pixels for the shader's sake: it needs a quad of a whole number of them.
/// </remarks>
public sealed partial class SupplyPodLandingZoneSystem : SharedSupplyPodLandingZoneSystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;

    private static readonly ProtoId<ShaderPrototype> PixelSnapShaderId = "KsPixelSnap";

    /// <summary>
    ///     Pixels across the layers' art, which is also how many world pixels they cover at scale 1.
    /// </summary>
    private const float ArtPixels = 32f;

    [SubscribeLocalEvent]
    private void OnIndicatorStartup(Entity<SupplyPodLandingZoneIndicatorComponent> entity, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(entity.Owner, out var spriteComponent))
            return;

        // One instance per layer: each carries its own pixel count, and the program's uniforms are shared.
        var shadersComponent = EnsureComp<SupplyPodLandingZoneShadersComponent>(entity.Owner);
        shadersComponent.ZoneShader = ProtoMan.Index(PixelSnapShaderId).InstanceUnique();
        shadersComponent.SliderShader = ProtoMan.Index(PixelSnapShaderId).InstanceUnique();

        spriteComponent.LayerSetShader(SupplyPodLandingZoneLayers.Zone, shadersComponent.ZoneShader);
        spriteComponent.LayerSetShader(SupplyPodLandingZoneLayers.Slider, shadersComponent.SliderShader);
    }

    [SubscribeLocalEvent]
    private void OnShadersShutdown(Entity<SupplyPodLandingZoneShadersComponent> entity, ref ComponentShutdown args)
    {
        entity.Comp.ZoneShader?.Dispose();
        entity.Comp.ZoneShader = null;

        entity.Comp.SliderShader?.Dispose();
        entity.Comp.SliderShader = null;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // The remainder keeps the motion smooth between ticks instead of stepping at the tick rate.
        var curTime = _gameTiming.CurTime + _gameTiming.TickRemainder;
        var eqe = EntityQueryEnumerator<SupplyPodLandingZoneIndicatorComponent, SpriteComponent, SupplyPodLandingZoneShadersComponent>();

        while (eqe.MoveNext(out var uid, out var indicatorComponent, out var spriteComponent, out var shadersComponent))
        {
            var fraction = indicatorComponent.Duration <= TimeSpan.Zero
                ? 1f
                : Math.Clamp((float)((curTime - indicatorComponent.StartTime).TotalSeconds / indicatorComponent.Duration.TotalSeconds), 0f, 1f);

            var spriteEntity = new Entity<SpriteComponent?>(uid, spriteComponent);
            var filtered = indicatorComponent.Filtered ? 1f : 0f;

            _spriteSystem.LayerSetRotation(spriteEntity, SupplyPodLandingZoneLayers.Zone, indicatorComponent.ZoneTurn * fraction);

            // Unlike the slider, the zone keeps its size.
            SetShaderParameters(shadersComponent.ZoneShader, ArtPixels, filtered);

            // Whole world pixels across, and gone once less than one is left.
            var sliderPixels = MathF.Round(ArtPixels * float.Lerp(indicatorComponent.SliderStartScale, indicatorComponent.SliderEndScale, fraction));
            var drawnPixels = MathF.Max(sliderPixels, 1f); // the engine refuses a zero scale; the layer is hidden instead
            var sliderScale = drawnPixels / ArtPixels;

            _spriteSystem.LayerSetVisible(spriteEntity, SupplyPodLandingZoneLayers.Slider, sliderPixels >= 1f);
            _spriteSystem.LayerSetScale(spriteEntity, SupplyPodLandingZoneLayers.Slider, new Vector2(sliderScale, sliderScale));
            SetShaderParameters(shadersComponent.SliderShader, drawnPixels, filtered);
        }
    }

    private static void SetShaderParameters(ShaderInstance? shader, float quadPixels, float filtered)
    {
        shader?.SetParameter("quadPixels", quadPixels);
        shader?.SetParameter("filtered", filtered);
    }
}
