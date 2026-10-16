using System.Numerics;
using Content.Shared._KS14.LockOn;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._KS14.LockOn;

/// <summary>
///     Draws shrinking corner brackets, visible to everyone, around the target of each <see cref="KsTargetLockOnComponent"/>
///         that has one locked, solid and pulsing once the lock completes. Each shooter locking a target also gets a
///         chevron on a ring around the brackets, in the direction of that shooter and pointing at the target, so the
///         number of chevrons is the number of lock-ons and each one shows where its threat is.
/// </summary>
public sealed partial class KsLockOnIndicatorOverlay : Overlay
{
    private const float StartHalfSize = 0.75f;
    private const float EndHalfSize = 0.3f;
    private const float BracketLength = 0.22f;
    private const float BracketThickness = 0.05f;
    private const float PulseFrequency = 4f;

    /// <summary>
    ///     Chevron ring radius: just outside the corners of the widest bracket box.
    /// </summary>
    private const float ChevronRingRadius = StartHalfSize * 1.4142135f + 0.14f;
    private const float ChevronLength = 0.2f;
    private const float ChevronWidth = 0.22f;

    /// <summary>
    ///     Each extra lock-on thickens the brackets by this fraction, up to <see cref="MaxThicknessScale"/>.
    /// </summary>
    private const float ThicknessPerExtraLock = 0.2f;
    private const float MaxThicknessScale = 2f;

    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedKsTargetLockOnSystem _lockOnSystem = default!;
    [Dependency] private EntityQuery<TransformComponent> _transformQuery = default!;

    // WorldSpaceBelowFOV is rendered after the lights have been applied (Clyde.HLR.cs: "Overlays WS" follows
    //      ApplyFovToBuffer and `_lightingReady = false`), so the reticle is not darkened.
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private struct Reticle
    {
        public Vector2 Center;
        public Angle GridRotation;
        public float HalfSize;
        public Color Color;
        public float Progress;
        public int LockCount;
    }

    /// <summary>
    ///     One shooter's marker. <see cref="Direction"/> is the unit vector from the target to the shooter in the
    ///         target's grid frame.
    /// </summary>
    private struct Chevron
    {
        public int ReticleIndex;
        public Vector2 Direction;
        public Color Color;
        public float Progress;
        public bool Locked;
    }

    // Scratch, rebuilt by every BeforeDraw and read only by the Draw that follows it.
    private readonly Dictionary<EntityUid, int> _reticleIndexByTarget = new();
    private readonly List<Reticle> _reticles = new();
    private readonly List<Chevron> _chevrons = new();
    private readonly Vector2[] _triangle = new Vector2[3];

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _reticles.Clear();
        _chevrons.Clear();
        _reticleIndexByTarget.Clear();

        var curTime = _gameTiming.CurTime;
        var cullBounds = args.WorldAABB.Enlarged(StartHalfSize);

        var enumerator = _entityManager.EntityQueryEnumerator<KsTargetLockOnComponent>();
        while (enumerator.MoveNext(out var shooterUid, out var lockOnComponent))
        {
            if (lockOnComponent.Target is not { } targetUid
                || !_transformQuery.TryComp(targetUid, out var targetTransform)
                || targetTransform.MapID != args.MapId)
                continue;

            var center = _transformSystem.GetWorldPosition(targetTransform);
            if (!cullBounds.Contains(center))
                continue;

            var gridRotation = _transformSystem.GetGrid(targetUid) is { } gridUid
                ? _transformSystem.GetWorldRotation(gridUid)
                : Angle.Zero;

            var color = lockOnComponent.IndicatorColor;
            var locked = curTime >= lockOnComponent.LockEndTime;
            float halfSize;
            float progress;

            if (locked)
            {
                progress = 1f;
                halfSize = EndHalfSize;

                color = color.WithAlpha(color.A * (0.55f + 0.45f * GetPulse(curTime)));
            }
            else
            {
                progress = _lockOnSystem.GetLockProgress((shooterUid, lockOnComponent));
                halfSize = float.Lerp(StartHalfSize, EndHalfSize, progress);
                color = color.WithAlpha(color.A * (0.4f + 0.4f * progress));
            }

            if (!_reticleIndexByTarget.TryGetValue(targetUid, out var reticleIndex))
            {
                reticleIndex = _reticles.Count;
                _reticleIndexByTarget[targetUid] = reticleIndex;
                _reticles.Add(new Reticle { Center = center, GridRotation = gridRotation, Progress = -1f });
            }

            var reticle = _reticles[reticleIndex];
            reticle.LockCount++;

            // The most advanced lock decides the brackets' size and colour.
            if (progress > reticle.Progress)
            {
                reticle.HalfSize = halfSize;
                reticle.Color = color;
                reticle.Progress = progress;
            }

            _reticles[reticleIndex] = reticle;

            // Direction to the shooter, turned from the map frame into the grid frame the reticle is drawn in.
            var direction = Vector2.UnitY;
            if (_transformQuery.TryComp(shooterUid, out var shooterTransform) && shooterTransform.MapID == args.MapId)
            {
                var toShooter = _transformSystem.GetWorldPosition(shooterTransform) - center;
                if (toShooter.LengthSquared() > 0.0001f)
                    direction = new Angle(-gridRotation.Theta).RotateVec(Vector2.Normalize(toShooter));
            }

            var chevronColor = lockOnComponent.IndicatorColor;
            if (locked)
                chevronColor = chevronColor.WithAlpha(chevronColor.A * (0.6f + 0.4f * GetPulse(curTime)));

            _chevrons.Add(new Chevron
            {
                ReticleIndex = reticleIndex,
                Direction = direction,
                Color = chevronColor,
                Progress = progress,
                Locked = locked,
            });
        }

        return _reticles.Count > 0;
    }

    private static float GetPulse(TimeSpan curTime)
    {
        return 0.5f + 0.5f * MathF.Sin((float)curTime.TotalSeconds * PulseFrequency * MathF.Tau);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var worldHandle = args.WorldHandle;

        worldHandle.UseShader(null);

        for (var reticleIndex = 0; reticleIndex < _reticles.Count; reticleIndex++)
        {
            var reticle = _reticles[reticleIndex];

            // Square to the target's grid, not the map: draw around the origin of the grid's frame.
            worldHandle.SetTransform(Matrix3Helpers.CreateTransform(reticle.Center, reticle.GridRotation));

            var thicknessScale = MathF.Min(1f + ThicknessPerExtraLock * (reticle.LockCount - 1), MaxThicknessScale);
            DrawBrackets(worldHandle, Vector2.Zero, reticle.HalfSize, BracketThickness * thicknessScale, reticle.Color);

            for (var chevronIndex = 0; chevronIndex < _chevrons.Count; chevronIndex++)
            {
                var chevron = _chevrons[chevronIndex];
                if (chevron.ReticleIndex == reticleIndex)
                    DrawChevron(worldHandle, chevron);
            }
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
    }

    /// <summary>
    ///     A triangle on the ring, tip towards the target. An outline always, filled in proportion to the lock's own
    ///         progress, and solid once locked.
    /// </summary>
    private void DrawChevron(DrawingHandleWorld worldHandle, in Chevron chevron)
    {
        var inward = -chevron.Direction;
        var sideways = new Vector2(-inward.Y, inward.X);
        var middle = chevron.Direction * ChevronRingRadius;

        _triangle[0] = middle + inward * (ChevronLength / 2f);
        _triangle[1] = middle - inward * (ChevronLength / 2f) + sideways * (ChevronWidth / 2f);
        _triangle[2] = middle - inward * (ChevronLength / 2f) - sideways * (ChevronWidth / 2f);

        var fillAlpha = chevron.Locked ? chevron.Color.A : chevron.Color.A * chevron.Progress;
        if (fillAlpha > 0.01f)
            worldHandle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _triangle, chevron.Color.WithAlpha(fillAlpha));

        var outlineColor = chevron.Color.WithAlpha(chevron.Locked ? chevron.Color.A : chevron.Color.A * 0.9f);
        worldHandle.DrawLine(_triangle[0], _triangle[1], outlineColor);
        worldHandle.DrawLine(_triangle[1], _triangle[2], outlineColor);
        worldHandle.DrawLine(_triangle[2], _triangle[0], outlineColor);
    }

    private static void DrawBrackets(DrawingHandleWorld worldHandle, Vector2 center, float halfSize, float thickness, Color color)
    {
        // Each corner is two arms along the box edges, drawn as thin rects.
        for (var xSign = -1; xSign <= 1; xSign += 2)
        {
            for (var ySign = -1; ySign <= 1; ySign += 2)
            {
                var corner = center + new Vector2(xSign * halfSize, ySign * halfSize);

                var horizontalEnd = corner - new Vector2(xSign * BracketLength, 0f);
                worldHandle.DrawRect(
                    new Box2(
                        MathF.Min(corner.X, horizontalEnd.X),
                        corner.Y - thickness / 2f,
                        MathF.Max(corner.X, horizontalEnd.X),
                        corner.Y + thickness / 2f),
                    color);

                var verticalEnd = corner - new Vector2(0f, ySign * BracketLength);
                worldHandle.DrawRect(
                    new Box2(
                        corner.X - thickness / 2f,
                        MathF.Min(corner.Y, verticalEnd.Y),
                        corner.X + thickness / 2f,
                        MathF.Max(corner.Y, verticalEnd.Y)),
                    color);
            }
        }
    }
}
