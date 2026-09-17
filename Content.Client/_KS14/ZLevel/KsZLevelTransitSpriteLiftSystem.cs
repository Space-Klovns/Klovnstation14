using Robust.Client.GameObjects;

namespace Content.Client._KS14.ZLevel;

/// <summary>
///     The post-animation half of making a transiting entity look like it is still up where it fell from.
///     By the time this runs the sprite offset and scale hold exactly the animation player's output — or the
///         clean base <see cref="KsZLevelTransitSpriteSystem"/> reset them to, if no animation ran — so the
///         compensation composes on top and no animation code ever has to know z-levels exist.
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
            _spriteSystem.SetOffset((uid, spriteComponent), spriteComponent.Offset + transitSpriteComponent.Lift);
            _spriteSystem.SetScale((uid, spriteComponent), spriteComponent.Scale * transitSpriteComponent.ScaleMultiplier);
        }
    }
}
