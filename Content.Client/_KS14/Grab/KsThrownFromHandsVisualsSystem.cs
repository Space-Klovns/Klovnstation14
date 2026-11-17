using System.Numerics;
using Content.Shared._KS14.Grab;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client._KS14.Grab;

/// <summary>
///     A grabbed person is thrown from where their thrower stands, not from where they were held. Their body is moved
///         there at once; this slides their sprite over from where it was drawn, so nothing is seen to jump.
/// </summary>
public sealed partial class KsThrownFromHandsVisualsSystem : EntitySystem
{
    [Dependency] private AnimationPlayerSystem _animationPlayerSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;

    private const string AnimationKey = "ks-thrown-from-hands";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<KsThrownFromHandsMessage>(OnThrownMessage);
    }

    /// <summary>
    ///     Someone else's throw, told of by the server.
    /// </summary>
    private void OnThrownMessage(KsThrownFromHandsMessage args)
    {
        if (TryGetEntity(args.Thrown, out var thrown) && TryComp<SpriteComponent>(thrown, out var sprite))
            Slide((thrown.Value, sprite), args.Offset, args.Duration);
    }

    /// <summary>
    ///     This client's own throw, as it predicts it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnThrown(Entity<SpriteComponent> thrown, ref KsThrownFromHandsEvent args)
    {
        Slide(thrown, args.Offset, args.Duration);
    }

    private void Slide(Entity<SpriteComponent> thrown, Vector2 offset, TimeSpan time)
    {
        if (time <= TimeSpan.Zero)
            return;

        // A slide still running ends where this one starts from: at the sprite's own offset.
        var restOffset = thrown.Comp.Offset;
        if (_animationPlayerSystem.HasRunningAnimation(thrown.Owner, AnimationKey))
        {
            _animationPlayerSystem.Stop(thrown.Owner, AnimationKey);
            restOffset = Vector2.Zero;
        }

        // The animation only writes its first frame on the next frame update. Their body has already moved, so
        // without this they would be drawn at the thrower for a frame, then back where they were, then slide.
        _spriteSystem.SetOffset(thrown.AsNullable(), restOffset + offset);

        var duration = (float)time.TotalSeconds;
        _animationPlayerSystem.Play(thrown.Owner, new Animation
        {
            Length = time,
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(restOffset + offset, 0f),
                        new AnimationTrackProperty.KeyFrame(restOffset, duration),
                    },
                },
            },
        }, AnimationKey);
    }
}
