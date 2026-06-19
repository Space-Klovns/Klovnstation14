using Content.Shared._KS14;
using Content.Shared._KS14.GenericSpriteFlick;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameStates;
using Robust.Shared.Reflection;
using Robust.Shared.Utility;

namespace Content.Client._KS14.GenericSpriteFlick;

public sealed class KsGenericSpriteFlickVisualizerSystem : EntitySystem
{
    [Dependency] private readonly IReflectionManager _reflectionManager = default!;
    [Dependency] private readonly AnimationPlayerSystem _animationPlayerSystem = default!;
    [Dependency] private readonly SpriteSystem _spriteSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsGenericSpriteFlickFinishStateComponent, ComponentHandleState>(OnFinishStateHandleState);
        SubscribeLocalEvent<KsGenericSpriteFlickComponent, AnimationCompletedEvent>(OnAnimationComplete);

        SubscribeAllEvent<KsSpriteFlickEvent>(OnEvent);
    }

    private void OnFinishStateHandleState(Entity<KsGenericSpriteFlickFinishStateComponent> entity, ref ComponentHandleState args)
    {
        if (args.Current is not KsGenericSpriteFlickFinishStateComponentState state)
            return;

        entity.Comp.FinishStates = state.FinishStates;

        if (!TryComp<AnimationPlayerComponent>(entity.Owner, out var animationPlayerComponent) ||
            !TryComp<SpriteComponent>(entity.Owner, out var spriteComponent))
            return;

        foreach (var ((animKey, layerKey), spriteState) in state.FinishStates)
        {
            if (_animationPlayerSystem.HasRunningAnimation(animationPlayerComponent, animKey))
                continue;

            var layerEnumKey = KsEnumHelpers.ParseKey(layerKey, out var isEnum, _reflectionManager);
            if (isEnum)
                _spriteSystem.LayerSetRsiState((entity, spriteComponent)!, layerEnumKey!, new RSI.StateId(spriteState));
            else
                _spriteSystem.LayerSetRsiState((entity, spriteComponent)!, layerKey, new RSI.StateId(spriteState));
        }
    }

    // Try to reset to next state
    private void OnAnimationComplete(Entity<KsGenericSpriteFlickComponent> entity, ref AnimationCompletedEvent args)
    {
        if (!entity.Comp.AnimKeyLayerKeyMap.TryGetValue(args.Key, out var layerObjectKey) ||
            !entity.Comp.NextStateMap.TryGetValue(layerObjectKey, out var state))
            return;

        if (!TryComp<SpriteComponent>(entity.Owner, out var spriteComponent))
            return;

        if (layerObjectKey is Enum layerEnumKey)
            _spriteSystem.LayerSetRsiState((entity, spriteComponent)!, layerEnumKey, state);
        else if (layerObjectKey is string layerStringKey)
            _spriteSystem.LayerSetRsiState((entity, spriteComponent)!, layerStringKey, state);
        else
            throw new ArgumentException($"Param was assignable to neither {typeof(Enum)} nor {typeof(string)}.", nameof(layerObjectKey));
    }

    private void OnEvent(KsSpriteFlickEvent args)
    {
        if (!TryGetEntity(args.Entity, out var uid) ||
            !TryComp<SpriteComponent>(uid.Value, out var spriteComponent))
            return;

        var parsedLayerKey = (object?)KsEnumHelpers.ParseKey(args.LayerKey, out _, _reflectionManager) ?? args.LayerKey;
        var animationKey = KsGenericSpriteFlickSystem.GetAnimationId(args.FlickState, args.LayerKey);

        if (!TryComp<AnimationPlayerComponent>(uid.Value, out var animationPlayerComponent) ||
            _animationPlayerSystem.HasRunningAnimation(animationPlayerComponent, animationKey))
            return;

        var flickComponent = EnsureComp<KsGenericSpriteFlickComponent>(uid.Value);
        var animation = flickComponent.CachedAnimations.GetOrNew((args.FlickState, parsedLayerKey), out var exists);
        flickComponent.AnimKeyLayerKeyMap[animationKey] = parsedLayerKey;

        if (!exists)
        {
            var state = _spriteSystem.GetState(new SpriteSpecifier.Rsi(spriteComponent.BaseRSI!.Path, args.FlickState));

            animation.Length = TimeSpan.FromSeconds(state.AnimationLength);
            animation.AnimationTracks.Add(
                new AnimationTrackSpriteFlick
                {
                    LayerKey = parsedLayerKey,
                    KeyFrames =
                    {
                        new AnimationTrackSpriteFlick.KeyFrame(new RSI.StateId(args.FlickState), default)
                    }
                }
            );
        }

        flickComponent.NextStateMap[parsedLayerKey] = args.FinishState ?? spriteComponent[parsedLayerKey].RsiState;
        _animationPlayerSystem.Play((uid.Value, animationPlayerComponent), animation, animationKey);
    }
}
