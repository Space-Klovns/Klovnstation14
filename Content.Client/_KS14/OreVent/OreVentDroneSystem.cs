using System.Numerics;
using Content.Shared._KS14.OreVent;
using Content.Shared.Rounding;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Animations;
using Robust.Shared.Timing;

namespace Content.Client._KS14.OreVent;

public sealed class OreVentDroneSystem : SharedOreVentDroneSystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SpriteSystem _spriteSystem = default!;
    [Dependency] private readonly AnimationPlayerSystem _animationPlayerSystem = default!;


    // I know, this is horrible. You can't stop me
    private const string IconEscapeState = "node_escape";
    private const string IconBaseProgressState = "progress_";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OreVentDroneComponent, AppearanceChangeEvent>(OnAppearanceChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var eqe = EntityQueryEnumerator<OreVentDroneComponent, SpriteComponent>();
        while (eqe.MoveNext(out var uid, out var droneComponent, out var spriteComponent))
        {
            if (droneComponent.VentUid == EntityUid.Invalid ||
                !TryComp<OreVentComponent>(droneComponent.VentUid, out var oreVentComponent))
                continue;

            if (!_spriteSystem.LayerMapTryGet((uid, spriteComponent), OreVentDroneVisualLayers.ProgressBar, out var layerIndex, logMissing: false))
                continue;

            if (!oreVentComponent.BeingTapped)
            {
                if (droneComponent.LastActiveProgressState != -1)
                    continue;

                droneComponent.LastActiveProgressState = -1;
                _spriteSystem.LayerSetVisible((uid, spriteComponent), layerIndex, false);

                continue;
            }

            var state = ContentHelpers.RoundToLevels(
                (oreVentComponent.TappingFinishedTime - _gameTiming.CurTime).TotalSeconds,
                oreVentComponent.ExtractionDuration.TotalSeconds,
                droneComponent.ProgressStates
            ) + 1;
            if (droneComponent.LastActiveProgressState != state)
                continue;

            droneComponent.LastActiveProgressState = state;
            _spriteSystem.LayerSetVisible((uid, spriteComponent), layerIndex, true);
            _spriteSystem.LayerSetRsiState((uid, spriteComponent), layerIndex, IconBaseProgressState + state); // oh no
        }
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
                _animationPlayerSystem.Play(entity.Owner, ArrivalAnimation, "arrival_offset");
                break;
            case OreVentDroneMovement.StartingUp:
                _animationPlayerSystem.Play(entity.Owner, PreEscapeFlickAnimation, "preescape_flick");
                break;
            case OreVentDroneMovement.Dipping:
                _animationPlayerSystem.Play(entity.Owner, EscapeAnimation, "escape_offset");
                break;
            default:
                return;
        }
    }

    private static readonly Animation PreEscapeFlickAnimation = new()
    {
        Length = TimeSpan.FromSeconds(1.9d),
        AnimationTracks =
        {
            new AnimationTrackSpriteFlick
            {
                LayerKey = OreVentDroneLayers.Progress,
                KeyFrames =
                {
                    new AnimationTrackSpriteFlick.KeyFrame(new RSI.StateId(IconEscapeState), default)
                }
            }
        }
    };

    private static readonly Animation ArrivalAnimation = new()
    {
        Length = TimeSpan.FromSeconds(2d),
        AnimationTracks =
        {
            new AnimationTrackComponentProperty
            {
                ComponentType = typeof(SpriteComponent),
                Property = nameof(SpriteComponent.Offset),
                InterpolationMode = AnimationInterpolationMode.Linear,
                KeyFrames =
                {
                    new AnimationTrackProperty.KeyFrame(new Vector2(0f, 12.5f), 0f),
                    new AnimationTrackProperty.KeyFrame(new Vector2(0f, 0f), 2f, easing: Easings.OutQuad),
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
                }
            }
        }
    };

    private static readonly Animation EscapeAnimation = new()
    {
        Length = TimeSpan.FromSeconds(2d),
        AnimationTracks =
        {
            new AnimationTrackComponentProperty
            {
                ComponentType = typeof(SpriteComponent),
                Property = nameof(SpriteComponent.Offset),
                InterpolationMode = AnimationInterpolationMode.Linear,
                KeyFrames =
                {
                    new AnimationTrackProperty.KeyFrame(new Vector2(0f, 0f), 0f),
                    new AnimationTrackProperty.KeyFrame(new Vector2(0f, 12.5f), 2f, easing: Easings.InQuad),
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
                    new AnimationTrackProperty.KeyFrame(Color.Transparent, 2f, easing: Easings.InQuad),
                }
            }
        }
    };
}

public enum OreVentDroneLayers : byte { Progress }
