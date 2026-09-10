using Content.Shared._KS14.Sprite;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Client._KS14.Sprite;

/// <summary>
///     Drives <see cref="KsSpriteFadeOutComponent"/>. Clientside only - the server just says when
///         the fade starts.
/// </summary>
public sealed partial class KsSpriteFadeOutSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var curTime = _gameTiming.CurTime;
        var eqe = EntityQueryEnumerator<KsSpriteFadeOutComponent, SpriteComponent>();

        while (eqe.MoveNext(out var uid, out var fadeComponent, out var spriteComponent))
        {
            if (fadeComponent.FadeStartTime == TimeSpan.MaxValue)
                continue;

            var elapsed = curTime - fadeComponent.FadeStartTime;
            if (elapsed < TimeSpan.Zero)
                continue;

            var spriteEntity = new Entity<SpriteComponent?>(uid, spriteComponent);

            if (!fadeComponent.Captured)
            {
                fadeComponent.InitialAlpha = spriteComponent.Color.A;
                fadeComponent.Captured = true;

                if (fadeComponent.FadeDrawDepth is { } fadeDrawDepth)
                    _spriteSystem.SetDrawDepth(spriteEntity, (int)fadeDrawDepth);
            }

            var fraction = fadeComponent.FadeDuration <= TimeSpan.Zero
                ? 1f
                : Math.Clamp((float)(elapsed.TotalSeconds / fadeComponent.FadeDuration.TotalSeconds), 0f, 1f);

            _spriteSystem.SetColor(
                spriteEntity,
                spriteComponent.Color.WithAlpha(float.Lerp(fadeComponent.InitialAlpha, fadeComponent.TargetAlpha, fraction))
            );
        }
    }
}
