using System.Numerics;
using Content.Shared._KS14.OreVent;
using Content.Shared._KS14.OreVent.Drone;
using Content.Shared.Rounding;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Animations;
using Robust.Shared.Timing;

namespace Content.Client._KS14.OreVent.Drone;

/*
    This has caused alot of torment, needlessly so

    Basically, drones used to move into view by animating the SpriteComponent.Offset
    But this proved to be very problematic for unknown reasons; the drone sprite would
        sort-of get culled when its starting position was out of view; it would randomly become invisible

    Somehow, this issue gets fixed by the drone sprite getting mutated in some unknown way,
        notably switching state to progress_1 or progress_2 or whatever with a delay after arrival
        will make the drone visible again. What the FUCK??!!

    This is a warning for anyone in the future
    I just resorted to using TransformComponent.LocalPosition

    -LCDC
*/

public sealed class OreVentDroneSystem : SharedOreVentDroneSystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SpriteSystem _spriteSystem = default!;
    [Dependency] private readonly AnimationPlayerSystem _animationPlayerSystem = default!;


    // I know, this is horrible. You can't stop me
    private const string IconEscapeState = "node_escape";
    private const string IconFlyingState = "node_flying";
    private const string IconBaseProgressState = "progress_";

    private const string ArrivalAnimationKey = "arrival_offset";
    private const string PreEscapeAnimationKey = "preescape_flick";
    private const string EscapeAnimationKey = "escape_offset";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OreVentDroneComponent, AppearanceChangeEvent>(OnAppearanceChanged);
        SubscribeLocalEvent<OreVentDroneComponent, AnimationCompletedEvent>(OnAnimationCompleted);
    }

    private void OnAppearanceChanged(Entity<OreVentDroneComponent> entity, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null ||
            !args.AppearanceData.TryGetValue(OreVentDroneVisuals.Movement, out var stateObj) ||
            stateObj is not OreVentDroneMovement state)
            return;

        if (entity.Comp.LastMovementState ==
            state)
            return;

        entity.Comp.LastMovementState = state;
        switch (state)
        {
            case OreVentDroneMovement.Arriving:
                var transformComponent = Transform(entity);
                var arrivalAnimation = new Animation()
                {
                    Length = TimeSpan.FromSeconds(2d),
                    AnimationTracks =
                    {
                        new AnimationTrackComponentProperty
                        {
                            ComponentType = typeof(TransformComponent),
                            Property = nameof(TransformComponent.LocalPosition),
                            InterpolationMode = AnimationInterpolationMode.Linear,
                            KeyFrames =
                            {
                                new AnimationTrackProperty.KeyFrame(transformComponent.LocalPosition + new Vector2(0f, 12.5f), 0f),
                                new AnimationTrackProperty.KeyFrame(transformComponent.LocalPosition, 2f, easing: Easings.OutQuad),
                            }
                        },
                        new AnimationTrackComponentProperty
                        {
                            ComponentType = typeof(SpriteComponent),
                            Property = nameof(SpriteComponent.Color),
                            InterpolationMode = AnimationInterpolationMode.Linear,
                            KeyFrames =
                            {
                                new AnimationTrackProperty.KeyFrame(Color.Transparent, 0f),
                                new AnimationTrackProperty.KeyFrame(Color.White, 1.5f, easing: Easings.OutQuad),
                                new AnimationTrackProperty.KeyFrame(Color.White, 2f, easing: Easings.OutQuad)
                            }
                        }
                    }
                };

                _animationPlayerSystem.Play(entity.Owner, arrivalAnimation, ArrivalAnimationKey);
                break;
            case OreVentDroneMovement.Dipping:
                _animationPlayerSystem.Play(entity.Owner, PreEscapeFlickAnimation, PreEscapeAnimationKey);
                break;
            default:
                return;
        }
    }

    private void OnAnimationCompleted(Entity<OreVentDroneComponent> entity, ref AnimationCompletedEvent args)
    {
        if (!args.Finished ||
            args.Key != PreEscapeAnimationKey)
            return;

        var transformComponent = Transform(entity);
        var escapeAnimation = new Animation()
        {
            Length = TimeSpan.FromSeconds(2d),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick
                {
                    LayerKey = OreVentDroneVisualLayers.Base,
                    KeyFrames =
                    {
                        new AnimationTrackSpriteFlick.KeyFrame(new RSI.StateId(IconFlyingState), default)
                    }
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(TransformComponent),
                    Property = nameof(TransformComponent.LocalPosition),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(transformComponent.LocalPosition, 0f),
                        new AnimationTrackProperty.KeyFrame(transformComponent.LocalPosition + new Vector2(0f, 12.5f), 2f, easing: Easings.InQuad)
                    }
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Color.White, 0f),
                        new AnimationTrackProperty.KeyFrame(Color.White, 0.5f),
                        new AnimationTrackProperty.KeyFrame(Color.Transparent, 2f, easing: Easings.InQuad)
                    }
                }
            }
        };

        _animationPlayerSystem.Play((entity.Owner, args.AnimationPlayer), escapeAnimation, EscapeAnimationKey);
        _spriteSystem.LayerSetVisible(entity.Owner, OreVentDroneVisualLayers.ProgressBar, false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var eqe = EntityQueryEnumerator<OreVentDroneComponent, SpriteComponent>();
        while (eqe.MoveNext(out var uid, out var droneComponent, out var spriteComponent))
        {
            if (droneComponent.VentUid is not { } ventUid ||
                !TryComp<OreVentComponent>(ventUid, out var oreVentComponent))
                continue;

            if (!_spriteSystem.LayerMapTryGet((uid, spriteComponent), OreVentDroneVisualLayers.ProgressBar, out var layerIndex, logMissing: false))
                continue;

            if (!oreVentComponent.BeingTapped ||
                droneComponent.LastMovementState != OreVentDroneMovement.Arriving)
            {
                if (droneComponent.LastActiveProgressState == -1)
                    continue;

                droneComponent.LastActiveProgressState = -1;
                _spriteSystem.LayerSetVisible((uid, spriteComponent), layerIndex, false);

                continue;
            }

            var invState = ContentHelpers.RoundToNearestLevels((oreVentComponent.TappingFinishedTime - _gameTiming.CurTime).TotalSeconds, oreVentComponent.ExtractionDuration.TotalSeconds, droneComponent.ProgressStates - 1);
            var state = droneComponent.ProgressStates - 1 - invState;
            if (droneComponent.LastActiveProgressState == state)
                continue;

            droneComponent.LastActiveProgressState = state;
            _spriteSystem.LayerSetRsiState((uid, spriteComponent), layerIndex, IconBaseProgressState + state); // oh no
            _spriteSystem.LayerSetVisible((uid, spriteComponent), layerIndex, true);
        }
    }

    private static readonly Animation PreEscapeFlickAnimation = new()
    {
        Length = TimeSpan.FromSeconds(1.9d),
        AnimationTracks =
        {
            new AnimationTrackSpriteFlick
            {
                LayerKey = OreVentDroneVisualLayers.Base,
                KeyFrames =
                {
                    new AnimationTrackSpriteFlick.KeyFrame(new RSI.StateId(IconEscapeState), default)
                }
            }
        }
    };
}

public enum OreVentDroneVisualLayers : byte
{
    Base,
    ProgressBar
}
