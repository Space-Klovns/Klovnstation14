using System.Numerics;
using Content.Shared._KS14.MedicalBeam;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Timing;

namespace Content.Client._KS14.MedicalBeam;

/// <summary>
///     Draws each medical beam from its holder to its target: the beam sprite, a strip one metre long, laid end to end
///         and animated, the last piece cut to length (as tg's <c>Beam()</c> lays its segments).
/// </summary>
public sealed class KsMedicalBeamOverlay : Overlay
{
    private readonly EntityManager _entityManager;
    private readonly IGameTiming _gameTiming;
    private readonly SpriteSystem _spriteSystem;
    private readonly SharedTransformSystem _transformSystem;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public KsMedicalBeamOverlay(EntityManager entityManager, IGameTiming gameTiming)
    {
        _entityManager = entityManager;
        _gameTiming = gameTiming;
        _spriteSystem = entityManager.System<SpriteSystem>();
        _transformSystem = entityManager.System<SharedTransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);

        var time = (float)_gameTiming.RealTime.TotalSeconds;
        var transformQuery = _entityManager.TransformQuery;
        var query = _entityManager.EntityQueryEnumerator<KsMedicalBeamGunComponent>();
        while (query.MoveNext(out var beamComponent))
        {
            if (beamComponent.Target is not { } targetUid ||
                beamComponent.User is not { } userUid ||
                !transformQuery.TryComp(userUid, out var userTransform) ||
                !transformQuery.TryComp(targetUid, out var targetTransform) ||
                userTransform.MapID != args.MapId ||
                targetTransform.MapID != args.MapId)
                continue;

            var from = _transformSystem.GetWorldPosition(userTransform);
            var to = _transformSystem.GetWorldPosition(targetTransform);
            var difference = to - from;
            var length = difference.Length();
            if (length < 0.01f)
                continue;

            var direction = difference / length;
            var angle = difference.ToWorldAngle();
            var texture = CurrentFrame(beamComponent, time);
            if (texture == null)
                continue;

            // One metre per piece; the last cut short.
            for (var start = 0f; start < length; start += 1f)
            {
                var pieceLength = MathF.Min(1f, length - start);
                var centre = from + direction * (start + pieceLength / 2f);
                var box = new Box2(-0.5f, -pieceLength / 2f, 0.5f, pieceLength / 2f).Translated(centre);
                var subRegion = new UIBox2(0f, 0f, texture.Width, texture.Height * pieceLength);
                handle.DrawTextureRectRegion(texture, new Box2Rotated(box, angle, centre), subRegion: subRegion);
            }
        }
    }

    private Texture? CurrentFrame(KsMedicalBeamGunComponent beamComponent, float time)
    {
        var state = _spriteSystem.GetState(beamComponent.BeamSprite);
        var frames = state.GetFrames(RsiDirection.South);
        if (frames.Length == 0)
            return null;

        var delays = state.GetDelays();
        if (delays.Length == 0)
            return frames[0];

        var total = 0f;
        foreach (var delay in delays)
            total += delay;

        var position = total > 0f ? time % total : 0f;
        for (var i = 0; i < delays.Length && i < frames.Length; i++)
        {
            if (position < delays[i])
                return frames[i];

            position -= delays[i];
        }

        return frames[^1];
    }
}
