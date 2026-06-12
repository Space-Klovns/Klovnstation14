using Content.Client.Trigger.Components;
using Content.Shared.Trigger;
using Content.Shared.Trigger.Components.Effects; // KS14
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Audio.Systems;

namespace Content.Client.Trigger.Systems;

public sealed class TimerTriggerVisualizerSystem : VisualizerSystem<TimerTriggerVisualsComponent>
{
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TimerTriggerVisualsComponent, ComponentInit>(OnComponentInit);
    }

    private void OnComponentInit(Entity<TimerTriggerVisualsComponent> ent, ref ComponentInit args)
    {
        ent.Comp.PrimingAnimation = new Animation
        {
            Length = TimeSpan.MaxValue,
            AnimationTracks = {
                new AnimationTrackSpriteFlick()
                {
                    LayerKey = TriggerVisualLayers.Base,
                    KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame(ent.Comp.PrimingSprite, 0f) }
                }
            },
        };

        if (ent.Comp.PrimingSound != null)
        {
            ent.Comp.PrimingAnimation.AnimationTracks.Add(
                new AnimationTrackPlaySound()
                {
                    KeyFrames = { new AnimationTrackPlaySound.KeyFrame(_audioSystem.ResolveSound(ent.Comp.PrimingSound), 0) }
                }
            );
        }
    }

    protected override void OnAppearanceChange(EntityUid uid, TimerTriggerVisualsComponent comp, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null
        || !TryComp<AnimationPlayerComponent>(uid, out var animPlayer))
            return;

        // KS14
        // Check for gas release visual states first, as they should override the timer states.
        if (AppearanceSystem.TryGetData<bool>(uid, ReleaseGasOnTriggerVisuals.Key, out var active, args.Component))
        {
            // Stop priming animation if it's running.
            if (AnimationSystem.HasRunningAnimation(uid, animPlayer, TimerTriggerVisualsComponent.AnimationKey))
                AnimationSystem.Stop(uid, animPlayer, TimerTriggerVisualsComponent.AnimationKey);

            var stateName = active ? comp.ActiveSprite : comp.SpentSprite;
            if (stateName != null && SpriteSystem.LayerMapTryGet((uid, args.Sprite), TriggerVisualLayers.Base, out var layerIndex, false))
            {
                if (args.Sprite[layerIndex].RsiState.Name != stateName)
                {
                    SpriteSystem.LayerSetRsiState((uid, args.Sprite), layerIndex, stateName);
                    SpriteSystem.LayerSetAutoAnimated((uid, args.Sprite), layerIndex, true);
                }
            }
            return;
        }

        if (!AppearanceSystem.TryGetData<TriggerVisualState>(uid, TriggerVisuals.VisualState, out var state, args.Component))
            state = TriggerVisualState.Unprimed;

        switch (state)
        {
            case TriggerVisualState.Primed:
                if (!AnimationSystem.HasRunningAnimation(uid, animPlayer, TimerTriggerVisualsComponent.AnimationKey))
                    AnimationSystem.Play((uid, animPlayer), comp.PrimingAnimation, TimerTriggerVisualsComponent.AnimationKey);
                break;
            case TriggerVisualState.Unprimed:
                if (AnimationSystem.HasRunningAnimation(uid, animPlayer, TimerTriggerVisualsComponent.AnimationKey)) // KS14
                    AnimationSystem.Stop(uid, animPlayer, TimerTriggerVisualsComponent.AnimationKey); // KS14

                if (SpriteSystem.LayerMapTryGet((uid, args.Sprite), TriggerVisualLayers.Base, out var layerIndex, false) &&
                    args.Sprite[layerIndex].RsiState.Name != comp.UnprimedSprite) // KS14
                {
                    SpriteSystem.LayerSetRsiState((uid, args.Sprite), layerIndex, comp.UnprimedSprite);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}

public enum TriggerVisualLayers : byte
{
    Base
}
