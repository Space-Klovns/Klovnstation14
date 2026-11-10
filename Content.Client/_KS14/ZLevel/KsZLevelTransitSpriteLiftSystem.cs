using Robust.Client.GameObjects;

namespace Content.Client._KS14.ZLevel;

/// <summary>
///     The post-animation half of making a transiting entity look like it is still up where it fell from.
///     By the time this runs the sprite holds exactly the animation player's output — or whatever
///         <see cref="KsZLevelTransitSpriteSystem"/> put back, if no animation ran — so the compensation
///         composes on top and no animation code ever has to know z-levels exist.
/// </summary>
public sealed partial class KsZLevelTransitSpriteLiftSystem : EntitySystem
{
    [Dependency] private SpriteSystem _spriteSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(AnimationPlayerSystem));
        UpdatesOutsidePrediction = true;
    }

    public override void FrameUpdate(float frameTime)
    {
        var enumerator = AllEntityQuery<KsZLevelTransitSpriteComponent, SpriteComponent>();
        while (enumerator.MoveNext(out var uid, out var transitSpriteComponent, out var spriteComponent))
        {
            // Record what the animation left before layering anything on top of it, so the pre-animation pass
            //      can put exactly that back next frame. This is what keeps an animation that finishes mid-fall
            //      - a stun's colour flash, say - from being frozen at whatever it was showing.
            transitSpriteComponent.PreLiftOffset = spriteComponent.Offset;
            transitSpriteComponent.PreLiftScale = spriteComponent.Scale;
            transitSpriteComponent.PreLiftColor = spriteComponent.Color;

            _spriteSystem.SetOffset((uid, spriteComponent), spriteComponent.Offset + transitSpriteComponent.Lift);
            _spriteSystem.SetScale((uid, spriteComponent), spriteComponent.Scale * transitSpriteComponent.ScaleMultiplier);

            // Scale the alpha the animation left rather than assigning a colour, so a fade never tints anything.
            var color = spriteComponent.Color;
            _spriteSystem.SetColor((uid, spriteComponent), color.WithAlpha(color.A * transitSpriteComponent.AlphaMultiplier));
        }
    }
}
