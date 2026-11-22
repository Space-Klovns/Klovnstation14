using System.Numerics;
using Content.Shared._KS14.Perks.Support;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Perks.Support;

/// <summary>
///     Draws each called rally (<see cref="KsRallyZoneComponent"/>): its edge, and a disc filling in from the middle
///         until it goes off; then a bright flash of the whole zone that fades.
/// </summary>
public sealed class KsRallyZoneOverlay : Overlay
{
    /// <summary>
    ///     How thick the edge is drawn, in metres, and in how many circles.
    /// </summary>
    private const float EdgeWidth = 0.16f;

    private const int EdgeCircles = 8;

    private readonly IEntityManager _entityManager;
    private readonly IGameTiming _gameTiming;
    private readonly SharedTransformSystem _transformSystem;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public KsRallyZoneOverlay(IEntityManager entityManager, IGameTiming gameTiming)
    {
        _entityManager = entityManager;
        _gameTiming = gameTiming;
        _transformSystem = entityManager.System<SharedTransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);

        var curTime = _gameTiming.CurTime + _gameTiming.TickRemainder;
        var query = _entityManager.EntityQueryEnumerator<KsRallyZoneComponent, TransformComponent>();
        while (query.MoveNext(out var zoneComponent, out var transformComponent))
        {
            if (transformComponent.MapID != args.MapId)
                continue;

            var centre = _transformSystem.GetWorldPosition(transformComponent);
            if (!args.WorldAABB.Enlarged(zoneComponent.Radius).Contains(centre))
                continue;

            var color = zoneComponent.Color;
            if (curTime < zoneComponent.ActivateTime)
            {
                var total = (zoneComponent.ActivateTime - zoneComponent.StartTime).TotalSeconds;
                var progress = total > 0 ? (float)Math.Clamp((curTime - zoneComponent.StartTime).TotalSeconds / total, 0, 1) : 1f;

                // The edge pulses faster as the moment comes.
                var pulse = 0.8f + 0.2f * MathF.Sin(progress * progress * 30f);
                DrawEdge(handle, centre, zoneComponent.Radius, color.WithAlpha(pulse));
                handle.DrawCircle(centre, zoneComponent.Radius, color.WithAlpha(0.12f));
                handle.DrawCircle(centre, zoneComponent.Radius * progress, color.WithAlpha(0.3f));
                DrawEdge(handle, centre, zoneComponent.Radius * progress, color.WithAlpha(0.7f));
                continue;
            }

            var fade = zoneComponent.FadeDuration.TotalSeconds > 0
                ? (float)Math.Clamp((curTime - zoneComponent.ActivateTime).TotalSeconds / zoneComponent.FadeDuration.TotalSeconds, 0, 1)
                : 1f;
            var alpha = 1f - fade;
            handle.DrawCircle(centre, zoneComponent.Radius, color.WithAlpha(0.55f * alpha));
            DrawEdge(handle, centre, zoneComponent.Radius * (1f + 0.25f * fade), Color.White.WithAlpha(alpha));
        }
    }

    private static void DrawEdge(DrawingHandleWorld handle, Vector2 centre, float radius, Color color)
    {
        for (var i = 0; i < EdgeCircles; i++)
        {
            var edgeRadius = radius - EdgeWidth * i / EdgeCircles;
            if (edgeRadius > 0f)
                handle.DrawCircle(centre, edgeRadius, color, filled: false);
        }
    }
}
