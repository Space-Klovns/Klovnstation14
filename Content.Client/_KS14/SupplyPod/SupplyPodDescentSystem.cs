using System.Numerics;
using Content.Shared._KS14.SupplyPod;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Client._KS14.SupplyPod;

public sealed class SupplyPodDescentSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly AnimationPlayerSystem _animationPlayerSystem = default!;
    [Dependency] private readonly SpriteSystem _spriteSystem = default!;

    private const string DescentAnimationKey = "poddescent";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActiveSupplyPodComponent, ComponentStartup>(OnActiveStartup);
        SubscribeLocalEvent<ActiveSupplyPodComponent, AnimationCompletedEvent>(OnAnimationCompleted);
    }

    private void OnActiveStartup(Entity<ActiveSupplyPodComponent> entity, ref ComponentStartup args)
    {
        // should never happen but whatever
        if (_animationPlayerSystem.HasRunningAnimation(entity.Owner, DescentAnimationKey))
            return;

        var supplyPodComponent = Comp<SupplyPodComponent>(entity.Owner);
        var transformComponent = Transform(entity);
        transformComponent.GridTraversal = false;

        // Make a sorry attempt at syncing client-state with server-state
        var countdown = entity.Comp.LaunchFinishTime - _gameTiming.CurTime;
        var countdownSeconds = (float)countdown.TotalSeconds;

        var angledOffset = entity.Comp.Angle.RotateVec(new Vector2(0f, supplyPodComponent.Height));
        if (TryComp<SpriteComponent>(entity, out var spriteComponent))
            _spriteSystem.SetRotation((entity, spriteComponent), spriteComponent.Rotation + entity.Comp.Angle);

        var originalColor = spriteComponent?.Color ?? Color.White;
        var arrivalAnimation = new Animation()
        {
            Length = entity.Comp.LaunchFinishTime - _gameTiming.CurTime,
            AnimationTracks =
                {
                    new AnimationTrackComponentProperty
                    {
                        ComponentType = typeof(TransformComponent),
                        Property = nameof(TransformComponent.LocalPosition),
                        InterpolationMode = AnimationInterpolationMode.Linear,
                        KeyFrames =
                        {
                            new AnimationTrackProperty.KeyFrame(transformComponent.LocalPosition + angledOffset, 0f),
                            new AnimationTrackProperty.KeyFrame(transformComponent.LocalPosition, countdownSeconds, easing: null),
                        }
                    },
                    new AnimationTrackComponentProperty
                    {
                        ComponentType = typeof(SpriteComponent),
                        Property = nameof(SpriteComponent.Color),
                        InterpolationMode = AnimationInterpolationMode.Linear,
                        KeyFrames =
                        {
                            new AnimationTrackProperty.KeyFrame(originalColor.WithAlpha(0f), 0f),
                            new AnimationTrackProperty.KeyFrame(originalColor, countdownSeconds * 0.5f, easing: null)
                        }
                    }
                }
        };

        _animationPlayerSystem.Play(entity.Owner, arrivalAnimation, DescentAnimationKey);
    }

    private void OnAnimationCompleted(Entity<ActiveSupplyPodComponent> entity, ref AnimationCompletedEvent args)
    {
        if (args.Key != DescentAnimationKey)
            return;

        Transform(entity).GridTraversal = true;
    }
}
