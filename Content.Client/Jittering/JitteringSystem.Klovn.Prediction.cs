using System.Numerics;
using Content.Shared.Jittering;
using Robust.Client.GameObjects;

namespace Content.Client.Jittering;

// A jitter the client predicts (a shove's stagger) is taken off by every server state that doesn't know of it yet and
// put back as the client predicts again, tick after tick for a round trip. Stock, each of those snapped the sprite
// back and started a new animation in a new direction, which read as a violent shake. So the animation is only
// stopped on the next frame, and only if the component hasn't come back by then; one that has carries on untouched.
public sealed partial class JitteringSystem
{
    /// <summary>
    ///     Entities that lost their jitter since the last frame, with what the lost component knew. Emptied every frame.
    /// </summary>
    private readonly Dictionary<EntityUid, (Vector2 StartOffset, Vector2 LastJitter)> _ksPendingStops = new();

    /// <summary>
    ///     Leaves the animation of a jitter that is going away for the next frame to stop.
    /// </summary>
    /// <returns>False for an entity being deleted, which has nothing left to stop later.</returns>
    private bool KsTryDeferStop(Entity<JitteringComponent> jittering)
    {
        if (MetaData(jittering).EntityLifeStage >= EntityLifeStage.Terminating)
            return false;

        _ksPendingStops[jittering] = (jittering.Comp.StartOffset, jittering.Comp.LastJitter);
        return true;
    }

    /// <summary>
    ///     Carries on the animation of a jitter taken off earlier this frame, rather than starting a new one.
    /// </summary>
    /// <returns>False if no jitter was taken off this entity this frame: this one is new.</returns>
    private bool KsTryResume(Entity<JitteringComponent> jittering)
    {
        if (!_ksPendingStops.Remove(jittering, out var kept))
            return false;

        jittering.Comp.StartOffset = kept.StartOffset;
        jittering.Comp.LastJitter = kept.LastJitter;

        // Nothing to carry on, if the animation ended just as the component went: start the next, from the kept offset.
        if (!_animationPlayer.HasRunningAnimation(jittering, _jitterAnimationKey) &&
            TryComp(jittering, out SpriteComponent? spriteComponent))
        {
            var animationPlayerComponent = EnsureComp<AnimationPlayerComponent>(jittering);
            _animationPlayer.Play((jittering, animationPlayerComponent), GetAnimation(jittering, spriteComponent),
                _jitterAnimationKey);
        }

        return true;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_ksPendingStops.Count == 0)
            return;

        // Whatever is still here got no jitter back: it has really ended.
        foreach (var (uid, kept) in _ksPendingStops)
        {
            if (TryComp(uid, out AnimationPlayerComponent? animationPlayerComponent))
                _animationPlayer.Stop(uid, animationPlayerComponent, _jitterAnimationKey);

            if (TryComp(uid, out SpriteComponent? spriteComponent))
                _sprite.SetOffset((uid, spriteComponent), kept.StartOffset);
        }

        _ksPendingStops.Clear();
    }
}
