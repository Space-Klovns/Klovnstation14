using System.Numerics;

namespace Content.Shared._KS14.Trail;

/// <summary>
///     Kept you waiting, huh?
/// </summary>
/// <remarks>
///     Holds no state and subscribes to nothing — it's the shared maths that both the client
///         overlay and anything spawning trails agree on.
/// </remarks>
public sealed partial class KsTrailSystem : EntitySystem
{
    /// <summary>
    ///     Position of a tile in the trail's own local frame. The trail runs along local +Y,
    ///         so the entity's rotation is what aims it.
    /// </summary>
    public Vector2 GetTileOffset(KsTrailComponent trailComponent, int index)
        => new(0f, trailComponent.Spacing * index);

    /// <summary>
    ///     Number of tiles the overlay should actually walk for this trail.
    /// </summary>
    public int GetDrawLength(KsTrailComponent trailComponent)
        => Math.Clamp(trailComponent.Length, 0, KsTrailComponent.MaxLength);

    /// <summary>
    ///     Final alpha for one tile: the trail's own alpha, gated by the progressive reveal
    ///         and tapered by the tail fade.
    /// </summary>
    /// <param name="sourceDistance">
    ///     How far along the trail axis the source currently sits, when it is still around to
    ///         ask. Drives the reveal directly, which keeps the trail glued to its source instead
    ///         of merely scheduled to agree with it.
    /// </param>
    public float GetTileAlpha(KsTrailComponent trailComponent, int index, TimeSpan curTime, float? sourceDistance = null)
    {
        var length = GetDrawLength(trailComponent);
        if (length <= 0 || index < 1 || index > length)
            return 0f;

        var alpha = trailComponent.Color.A;

        // Progressive reveal: the tile furthest from the origin shows up first, so the trail
        // appears to be drawn by whatever is travelling down it.
        if (sourceDistance is { } distance)
        {
            // Everything the source has already passed is carved; everything beyond it isn't
            // there yet.
            if (trailComponent.Spacing * index < distance)
                return 0f;
        }
        else if (trailComponent.RevealDuration > TimeSpan.Zero)
        {
            var revealFraction = (float)(length - index) / length;
            var revealTime = trailComponent.RevealStartTime + trailComponent.RevealDuration * revealFraction;

            if (curTime < revealTime)
                return 0f;
        }

        // Ramp the whole trail up from nothing, so it doesn't snap to full brightness while
        // its source is still fading in at the head of it.
        if (trailComponent.RevealDuration > TimeSpan.Zero)
        {
            var fadeInDuration = trailComponent.RevealDuration * trailComponent.RevealFadeInFraction;
            if (fadeInDuration > TimeSpan.Zero)
            {
                var elapsed = curTime - trailComponent.RevealStartTime;
                alpha *= Math.Clamp((float)(elapsed / fadeInDuration), 0f, 1f);
            }
        }

        // Tail fade: taper the far end so the cut-off isn't a hard edge.
        var tailFadeTiles = trailComponent.TailFadeTiles;
        if (tailFadeTiles > 0 && index > length - tailFadeTiles)
            alpha *= (float)(length - index + 1) / (tailFadeTiles + 1);

        return alpha;
    }

    /// <summary>
    ///     Kicks off the trail's death animation. Safe to call on a trail that already has a
    ///         <see cref="KsTrailFadeComponent"/> from its prototype — the prototype's tuning is kept.
    /// </summary>
    public void StartFade(EntityUid trailUid, TimeSpan curTime)
    {
        var fadeComponent = EnsureComp<KsTrailFadeComponent>(trailUid);
        fadeComponent.StartTime = curTime;

        Dirty(trailUid, fadeComponent);
    }
}
