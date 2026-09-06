using System.Numerics;
using Content.Shared._KS14.SupplyPod;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;
using Robust.Shared.Timing;

namespace Content.Client._KS14.SupplyPod;

public sealed partial class SupplyPodDescentSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private AnimationPlayerSystem _animationPlayerSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;

    private const string DescentAnimationKey = "poddescent";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActiveSupplyPodComponent, AnimationCompletedEvent>(OnAnimationCompleted);
    }

    /// <summary>
    ///     Starts (or restarts) the flight animation for whichever leg the pod is currently on.
    ///         A launched pod flips from ascent to descent without the component ever going away,
    ///         so this is driven by the arrival time changing rather than by component startup.
    /// </summary>
    public void DoStartup(Entity<ActiveSupplyPodComponent> entity)
    {
        // Already animating this exact leg.
        if (entity.Comp.AnimatedFinishTime == entity.Comp.LaunchFinishTime)
            return;

        // Set at impact, right before the component is torn off. Nothing left to animate.
        if (entity.Comp.LaunchFinishTime == TimeSpan.MaxValue)
            return;

        // Make a sorry attempt at syncing client-state with server-state
        var countdown = entity.Comp.LaunchFinishTime - _gameTiming.CurTime;
        if (countdown <= TimeSpan.Zero)
            return;

        entity.Comp.AnimatedFinishTime = entity.Comp.LaunchFinishTime;

        // The previous leg's animation is normally finished by now, but a pod that turned around
        // out of view can come back with one still running.
        if (_animationPlayerSystem.HasRunningAnimation(entity.Owner, DescentAnimationKey))
            _animationPlayerSystem.Stop(entity.Owner, DescentAnimationKey);

        var supplyPodComponent = Comp<SupplyPodComponent>(entity.Owner);
        var transformComponent = Transform(entity);
        transformComponent.GridTraversal = false;

        var countdownSeconds = (float)countdown.TotalSeconds;

        // Anchoring on the networked destination rather than wherever the sprite currently sits
        // matters on the second leg: the pod is moved to its dropoff and turned around in the same
        // tick the animation restarts, and the animation itself has been writing LocalPosition.
        var groundPosition = transformComponent.LocalPosition;
        if (entity.Comp.DestinationCoordinates.EntityId == transformComponent.ParentUid)
            groundPosition = entity.Comp.DestinationCoordinates.Position;

        var angledOffset = entity.Comp.Angle.RotateVec(new Vector2(0f, supplyPodComponent.Height));
        var airPosition = groundPosition + angledOffset;

        var ascending = entity.Comp.Ascending;

        TryComp<SpriteComponent>(entity, out var spriteComponent);

        // Read off the sprite only the first time. By the end of an ascent the animation has driven
        // the sprite to fully transparent, and the descent leg would otherwise take that as the
        // colour to fade back in to - leaving the pod invisible all the way down.
        entity.Comp.OriginalColor ??= spriteComponent?.Color ?? Color.White;

        var originalColor = entity.Comp.OriginalColor.Value;
        var transparentColor = originalColor.WithAlpha(0f);

        var arrivalAnimation = new Animation()
        {
            Length = countdown,
            AnimationTracks =
                {
                    new AnimationTrackComponentProperty
                    {
                        ComponentType = typeof(TransformComponent),
                        Property = nameof(TransformComponent.LocalPosition),
                        InterpolationMode = AnimationInterpolationMode.Linear,
                        KeyFrames =
                        {
                            new AnimationTrackProperty.KeyFrame(ascending ? groundPosition : airPosition, 0f),
                            new AnimationTrackProperty.KeyFrame(ascending ? airPosition : groundPosition, countdownSeconds, easing: null),
                        }
                    },
                    // Falling pods fade in over the first half of their descent; rising ones fade
                    // back out over the second half of their climb.
                    new AnimationTrackComponentProperty
                    {
                        ComponentType = typeof(SpriteComponent),
                        Property = nameof(SpriteComponent.Color),
                        InterpolationMode = AnimationInterpolationMode.Linear,
                        KeyFrames = ascending
                            ? new()
                            {
                                new AnimationTrackProperty.KeyFrame(originalColor, 0f),
                                new AnimationTrackProperty.KeyFrame(originalColor, countdownSeconds * 0.5f, easing: null),
                                new AnimationTrackProperty.KeyFrame(transparentColor, countdownSeconds * 0.5f, easing: null)
                            }
                            : new()
                            {
                                new AnimationTrackProperty.KeyFrame(transparentColor, 0f),
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

    /// <summary>
    ///     The pod is done flying, so whatever the animation left on the sprite is handed back.
    ///         A pod whose flight was cut short mid-fade would otherwise stay part-transparent.
    /// </summary>
    public void DoShutdown(Entity<ActiveSupplyPodComponent> entity)
    {
        if (entity.Comp.OriginalColor is not { } originalColor || TerminatingOrDeleted(entity.Owner))
            return;

        if (_animationPlayerSystem.HasRunningAnimation(entity.Owner, DescentAnimationKey))
            _animationPlayerSystem.Stop(entity.Owner, DescentAnimationKey);

        _spriteSystem.SetColor(entity.Owner, originalColor);
    }
}
