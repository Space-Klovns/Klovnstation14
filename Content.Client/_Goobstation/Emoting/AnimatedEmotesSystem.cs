using System.Numerics;
using Content.Shared._Goobstation.Emoting;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client._Goobstation.Emoting;

public sealed partial class AnimatedEmotesSystem : EntitySystem
{
    [Dependency] private readonly AnimationPlayerSystem _anim = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AnimatedEmotesComponent, AnimationEmoteEvent>(OnAnimationEmoteEvent);
    }

    public void PlayEmote(EntityUid uid, Animation anim, string animationKey = "emoteAnimKeyId")
    {
        if (_anim.HasRunningAnimation(uid, animationKey))
            return;

        _anim.Play(uid, anim, animationKey);
    }

    private void OnAnimationEmoteEvent(Entity<AnimatedEmotesComponent> ent, ref AnimationEmoteEvent args)
    {
        PlayEmote(ent, Animations[args.AnimationKey], animationKey: args.AnimationKey);
    }

    private static readonly Dictionary<string, Animation> Animations = new() {
        { "emoteAnimFlip", new()
            {
                Length = TimeSpan.FromMilliseconds(500),
                AnimationTracks =
                    {
                        new AnimationTrackComponentProperty
                        {
                            ComponentType = typeof(SpriteComponent),
                            Property = nameof(SpriteComponent.Rotation),
                            InterpolationMode = AnimationInterpolationMode.Linear,
                            KeyFrames =
                            {
                                new AnimationTrackProperty.KeyFrame(Angle.Zero, 0f),
                                new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(180), 0.25f),
                                new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(360), 0.25f),
                            }
                        }
                    }
            }! },
        { "emoteAnimSpin", new()
            {
                Length = TimeSpan.FromMilliseconds(600),
                AnimationTracks =
                    {
                        new AnimationTrackComponentProperty
                        {
                            ComponentType = typeof(TransformComponent),
                            Property = nameof(TransformComponent.LocalRotation),
                            InterpolationMode = AnimationInterpolationMode.Linear,
                            KeyFrames =
                            {
                                new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(0), 0f),
                                new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(90), 0.075f),
                                new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(180), 0.075f),
                                new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(270), 0.075f),
                                new AnimationTrackProperty.KeyFrame(Angle.Zero, 0.075f),
                                new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(90), 0.075f),
                                new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(180), 0.075f),
                                new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(270), 0.075f),
                                new AnimationTrackProperty.KeyFrame(Angle.Zero, 0.075f),
                            }
                        }
                    }
            } },
        { "emoteAnimJump", new()
            {
                Length = TimeSpan.FromMilliseconds(250),
                AnimationTracks =
                    {
                        new AnimationTrackComponentProperty
                        {
                            ComponentType = typeof(SpriteComponent),
                            Property = nameof(SpriteComponent.Offset),
                            InterpolationMode = AnimationInterpolationMode.Cubic,
                            KeyFrames =
                            {
                                new AnimationTrackProperty.KeyFrame(Vector2.Zero, 0f),
                                new AnimationTrackProperty.KeyFrame(new Vector2(0, .20f), 0.125f),
                                new AnimationTrackProperty.KeyFrame(Vector2.Zero, 0.125f),
                            }
                        }
                    }
            } }
    };
}
