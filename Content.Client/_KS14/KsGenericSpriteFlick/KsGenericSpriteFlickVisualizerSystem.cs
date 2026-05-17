using Content.Shared._KS14.GenericSpriteFlick;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Utility;

namespace Content.Client._KS14.GenericSpriteFlick;

public sealed class KsGenericSpriteFlickVisualizerSystem : EntitySystem
{
    [Dependency] private readonly AnimationPlayerSystem _animationPlayerSystem = default!;
    [Dependency] private readonly SpriteSystem _spriteSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeAllEvent<KsSpriteFlickEvent>(OnEvent);
    }

    private void OnEvent(KsSpriteFlickEvent args)
    {
        if (!TryGetEntity(args.Entity, out var uid) ||
            !TryComp<SpriteComponent>(uid.Value, out var spriteComponent))
            return;

        var animationKey = args.State + args.LayerKey.ToString() + " ksgenericspriteflick";
        if (!TryComp<AnimationPlayerComponent>(uid.Value, out var animationPlayerComponent) ||
            !_animationPlayerSystem.HasRunningAnimation(animationPlayerComponent, animationKey))
            return;

        var flickComponent = EnsureComp<KsGenericSpriteFlickComponent>(uid.Value);
        var animation = flickComponent.CachedAnimations.GetOrNew((args.State, args.LayerKey), out var exists);

        if (!exists)
        {
            var state = _spriteSystem.GetState(new SpriteSpecifier.Rsi(spriteComponent.BaseRSI!.Path, args.State));

            animation.Length = TimeSpan.FromSeconds(state.AnimationLength);
            animation.AnimationTracks.Add(new AnimationTrackSpriteFlick
            {
                LayerKey = args.LayerKey,
                KeyFrames =
                {
                    new AnimationTrackSpriteFlick.KeyFrame(new RSI.StateId(args.State), default)
                }
            });
        }

        _animationPlayerSystem.Play((uid.Value, animationPlayerComponent), animation, args.State + args.LayerKey.ToString() + " ksgenericspriteflick");
    }
}
