using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client._KS14.Portal;
using Content.Shared._KS14.Portal;
using Content.Shared.Examine;
using Content.Shared.Teleportation.Components;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Profiling;
using Robust.Shared.Timing;

namespace Content.Client.Viewport
{
    /// <summary>
    ///     View portal rendering: before the viewer's own pass, the surroundings of each visible portal's
    ///         destination are rendered through a purpose-built eye into a viewport of the portal's own, for
    ///         <see cref="KsPortalOverlay"/> to composite into the portal's window.
    /// </summary>
    /// <remarks>
    ///     Each portal view covers the same world area as the main eye, at a fraction of its resolution
    ///         (<see cref="KsPortalRenderSystem.ResolutionScale"/>), and is placed by world coordinates, so no copy
    ///         is needed and the main viewport is never touched by the pre-pass.
    ///     Viewports are pooled and outlive the frame they were last used in by <see cref="PortalViewportLifetime"/>,
    ///         so a portal flickering in and out of view does not create and destroy full-size targets.
    ///     Z-levels: the pre-pass renders a single plain pass of the destination map no matter whether the
    ///         z-level loop runs. The overlay composites during the viewer's own map pass of that loop only
    ///         (it filters on the portal's map), so lower levels never show a portal's view. Mid-transit
    ///         the viewer's own pass is drawn through a scaled eye; the capture is still placed by world
    ///         coordinates, so it lines up but is sampled at the unscaled resolution.
    ///     Loop safety: <see cref="KsPortalRenderSystem.PassActive"/> is set for the whole pre-pass, which switches
    ///         off the portal overlay and every overlay that must not run in a portal view, and the captures are
    ///         only handed over after the last pre-pass render, so nothing here re-enters itself.
    /// </remarks>
    public sealed partial class ScalingViewport
    {
        /// <summary>
        ///     How long a portal viewport is kept after it was last used.
        /// </summary>
        private static readonly TimeSpan PortalViewportLifetime = TimeSpan.FromSeconds(2);

        /// <summary>
        ///     Windows taking up less of the view than this are not worth a render.
        /// </summary>
        private const float MinimumWindowFraction = 0.0005f;

        /// <summary>
        ///     Windows taking up less of the view than this count as far, for <see cref="KsPortalRenderSystem.FarUpdateInterval"/>.
        /// </summary>
        private const float FarWindowFraction = 0.08f;

        private sealed class PortalViewport
        {
            public IClydeViewport Viewport = default!;
            public Vector2i Size;
            public Vector2 RenderScale;
            public TimeSpan IdleSince;
            public bool UsedThisFrame;

            public bool HasCapture;
            public long CaptureFrame;
            public Vector2 CaptureEyeCentre;
            public Angle CaptureEyeRotation;
            public Vector2 CaptureEyeScale;
            public MapId CaptureMapId;
        }

        [Dependency] private ProfManager _portalProf = default!;
        [Dependency] private IGameTiming _portalTiming = default!;

        private readonly Dictionary<EntityUid, PortalViewport> _portalViewports = [];
        private readonly List<PortalViewport> _portalFreeViewports = [];
        private readonly List<EntityUid> _portalReleaseScratch = [];
        private readonly List<PortalCandidate> _portalCandidates = [];
        private readonly List<KsPortalDraw> _portalDraws = [];
        private long _portalFrame;

        private readonly Robust.Shared.Graphics.Eye _portalEye = new Robust.Shared.Graphics.Eye()
        {
            DrawFov = false,
        };

        private KsPortalRenderSystem? _portalSystem;
        private SharedTransformSystem? _portalTransformSystem;

        private readonly record struct PortalCandidate(
            EntityUid Uid,
            EntityUid Destination,
            float DistanceSquared,
            Vector2 Position,
            Angle Rotation,
            bool Far,
            List<Vector2> Triangles);

        private readonly List<List<Vector2>> _portalTrianglePool = [];
        private ExamineSystemShared? _portalExamineSystem;

        private void DrawPortalPrePass(IRenderHandle handle)
        {
            _portalDraws.Clear();
            _portalFrame++;

            foreach (var entry in _portalViewports.Values)
                entry.UsedThisFrame = false;

            _portalSystem ??= _entityManager.System<KsPortalRenderSystem>();

            using (_portalProf.Group("KsPortal pre-pass"))
            {
                var rendered = 0;

                if (_portalSystem.Enabled &&
                    _portalSystem.MaxRendered > 0 &&
                    _eye != null &&
                    _eye.Position.MapId != MapId.Nullspace &&
                    _viewport != null)
                {
                    _portalTransformSystem ??= _entityManager.System<SharedTransformSystem>();

                    using (_portalProf.Group("KsPortal candidates"))
                    {
                        CollectPortalCandidates(_eye);
                    }

                    KsPortalRenderSystem.PassActive = true;

                    try
                    {
                        var drawn = 0;
                        foreach (var candidate in _portalCandidates)
                        {
                            if (drawn >= _portalSystem.MaxRendered)
                                break;

                            if (!PreparePortal(_eye, candidate, out var wasRendered))
                                continue;

                            drawn++;

                            if (wasRendered)
                                rendered++;
                        }
                    }
                    finally
                    {
                        KsPortalRenderSystem.PassActive = false;
                    }
                }

                _portalProf.WriteValue("KsPortal rendered", ProfData.Int32(rendered));
                _portalProf.WriteValue("KsPortal visible", ProfData.Int32(_portalDraws.Count));
            }

            ReleaseUnusedPortalViewports();

            if (_portalDraws.Count > 0)
                _portalSystem.SetActive(_viewport!, _portalDraws);
        }

        private void EndPortalPass()
        {
            // A pointer left behind would let another viewport composite this one's captures.
            _portalSystem?.ClearActive();
        }

        private void CollectPortalCandidates(IEye eye)
        {
            _portalCandidates.Clear();

            var eyeCentre = eye.Position.Position + eye.Offset;
            var worldSize = GetPortalWorldSize(eye);
            var eyeBounds = KsPortalTransform.GetEyeWorldBounds(eyeCentre, eye.Rotation, worldSize).CalcBoundingBox();
            var mapId = eye.Position.MapId;

            // The viewer is the player's own body when it is on this map, which is where the occluder-like
            //      shadow is cast from; a camera or a ghost orbiting elsewhere falls back to the eye.
            var viewerPosition = eyeCentre;
            if (_playerManager.LocalEntity is { } localUid &&
                _entityManager.TryGetComponent<TransformComponent>(localUid, out var localTransform) &&
                localTransform.MapID == mapId)
                viewerPosition = _portalTransformSystem!.GetWorldPosition(localTransform);

            _portalExamineSystem ??= _entityManager.System<ExamineSystemShared>();
            var extrusion = eyeBounds.Width + eyeBounds.Height + (viewerPosition - eyeCentre).Length() + 10f;
            var poolIndex = 0;
            var farDistanceSquared = worldSize.X * worldSize.X * 0.25f;

            var enumerator = _entityManager.EntityQueryEnumerator<KsPortalViewComponent, LinkedEntityComponent, TransformComponent>();
            while (enumerator.MoveNext(out var portalUid, out var viewComponent, out var linkedComponent, out var transformComponent))
            {
                if (!viewComponent.Enabled ||
                    transformComponent.MapID != mapId ||
                    linkedComponent.LinkedEntities.Count == 0)
                    continue;

                var destinationUid = default(EntityUid);
                foreach (var linked in linkedComponent.LinkedEntities)
                {
                    destinationUid = linked;
                    break;
                }

                if (destinationUid == portalUid || !destinationUid.IsValid())
                    continue;

                var (position, rotation) = _portalTransformSystem!.GetWorldPositionRotation(transformComponent);
                KsPortalTransform.GetSegment(position, rotation, viewComponent.Width, out var segmentStart, out var segmentEnd);

                var segmentBounds = new Box2(
                    Vector2.Min(segmentStart, segmentEnd),
                    Vector2.Max(segmentStart, segmentEnd)).Enlarged(0.01f);
                if (!segmentBounds.Intersects(eyeBounds))
                    continue;

                // Windows too small to see are not worth a render, let alone a ray cast.
                var fraction = KsPortalTransform.EstimateWindowFraction(viewerPosition, segmentStart, segmentEnd, eyeCentre, worldSize);
                if (fraction < MinimumWindowFraction)
                    continue;

                // Walls in front of the portal must not reveal what is behind it. Done before the window is
                //      built, so a portal behind a wall costs nothing more.
                var portalCoordinates = new MapCoordinates(position, mapId);
                if (!_portalExamineSystem.InRangeUnOccluded(
                        new MapCoordinates(viewerPosition, mapId), portalCoordinates, range: 0f, predicate: null))
                    continue;

                if (poolIndex == _portalTrianglePool.Count)
                    _portalTrianglePool.Add([]);

                var triangles = _portalTrianglePool[poolIndex];

                // A viewer right on the line of the doorway has no shadow to speak of.
                if (!KsPortalTransform.TryGetVisibleWindow(viewerPosition, segmentStart, segmentEnd, extrusion, triangles))
                    continue;

                poolIndex++;

                var distanceSquared = (position - eyeCentre).LengthSquared();

                _portalCandidates.Add(new PortalCandidate(
                    portalUid,
                    destinationUid,
                    distanceSquared,
                    position,
                    rotation,
                    fraction < FarWindowFraction || distanceSquared > farDistanceSquared,
                    triangles));
            }

            _portalCandidates.Sort(static (first, second) => first.DistanceSquared.CompareTo(second.DistanceSquared));
        }

        /// <summary>
        ///     The world area the main viewport shows through an eye.
        /// </summary>
        private Vector2 GetPortalWorldSize(IEye eye)
        {
            return (Vector2)_viewport!.Size / (EyeManager.PixelsPerMeter * _viewport.RenderScale * eye.Scale);
        }

        /// <summary>
        ///     Makes sure the portal's viewport holds a current picture, rendering it unless a recent one is still
        ///         good, and queues the window for the overlay.
        /// </summary>
        private bool PreparePortal(IEye eye, PortalCandidate candidate, out bool wasRendered)
        {
            wasRendered = false;
            var destinationUid = candidate.Destination;

            if (!_entityManager.EntityExists(destinationUid) ||
                !_entityManager.TryGetComponent<TransformComponent>(destinationUid, out var destinationTransform) ||
                destinationTransform.MapID == MapId.Nullspace ||
                (_entityManager.GetComponent<MetaDataComponent>(destinationUid).Flags & MetaDataFlags.Detached) != 0)
                return false;

            var (destinationPosition, destinationRotation) =
                _portalTransformSystem!.GetWorldPositionRotation(destinationTransform);

            var mainEyeCentre = eye.Position.Position + eye.Offset;
            KsPortalTransform.GetPortalEye(
                mainEyeCentre,
                eye.Rotation,
                candidate.Position,
                candidate.Rotation,
                destinationPosition,
                destinationRotation,
                out var eyeCentre,
                out var eyeRotation);

            var entry = AcquirePortalViewport(candidate.Uid);
            entry.UsedThisFrame = true;

            if (!CanReuseCapture(entry, candidate.Far, eyeCentre, eyeRotation, eye.Scale, destinationTransform.MapID))
            {
                using (_portalProf.Group("KsPortal render"))
                {
                    _portalEye.Position = new MapCoordinates(eyeCentre, destinationTransform.MapID);
                    _portalEye.Offset = Vector2.Zero;
                    _portalEye.Rotation = eyeRotation;
                    _portalEye.Scale = eye.Scale;
                    _portalEye.DrawFov = false;
                    _portalEye.DrawLight = eye.DrawLight && _portalSystem!.Lighting;

                    entry.Viewport.Eye = _portalEye;
                    entry.Viewport.Render();
                    entry.Viewport.Eye = null;
                }

                entry.HasCapture = true;
                entry.CaptureFrame = _portalFrame;
                entry.CaptureEyeCentre = eyeCentre;
                entry.CaptureEyeRotation = eyeRotation;
                entry.CaptureEyeScale = eye.Scale;
                entry.CaptureMapId = destinationTransform.MapID;
                wasRendered = true;
            }

            // The capture covers the main eye's world area, whatever the resolution it was rendered at.
            if (!KsPortalTransform.TryGetWindowRegion(
                    mainEyeCentre, eye.Rotation, GetPortalWorldSize(eye), candidate.Triangles, out var fullBox, out var region))
                return wasRendered;

            var texture = entry.Viewport.RenderTarget.Texture;

            _portalDraws.Add(new KsPortalDraw(
                texture,
                candidate.Triangles,
                new Box2Rotated(region, -eye.Rotation, mainEyeCentre),
                KsPortalTransform.GetTextureSubRegion(fullBox, region, (Vector2)texture.Size),
                eye.Position.MapId));

            return true;
        }

        /// <summary>
        ///     Whether a far portal's picture from a few frames ago can be shown again. It is placed by world
        ///         position, so it is only right while the eye it was rendered for has not moved.
        /// </summary>
        private bool CanReuseCapture(
            PortalViewport entry,
            bool far,
            Vector2 eyeCentre,
            Angle eyeRotation,
            Vector2 eyeScale,
            MapId mapId)
        {
            var interval = _portalSystem!.FarUpdateInterval;

            return far &&
                   interval > 1 &&
                   entry.HasCapture &&
                   _portalFrame - entry.CaptureFrame < interval &&
                   entry.CaptureMapId == mapId &&
                   entry.CaptureEyeScale == eyeScale &&
                   (entry.CaptureEyeCentre - eyeCentre).LengthSquared() < 1e-8f &&
                   MathF.Abs((float)Angle.ShortestDistance(entry.CaptureEyeRotation, eyeRotation)) < 1e-5f;
        }

        /// <summary>
        ///     The viewport a portal renders into: its own from earlier if it still has one, otherwise an idle
        ///         one of the right size, otherwise a new one.
        /// </summary>
        private PortalViewport AcquirePortalViewport(EntityUid portalUid)
        {
            KsPortalTransform.GetReducedViewport(
                _viewport!.Size,
                _viewport.RenderScale,
                _portalSystem!.ResolutionScale,
                out var size,
                out var renderScale);

            if (_portalViewports.TryGetValue(portalUid, out var existing))
            {
                if (existing.Size == size && existing.RenderScale == renderScale)
                    return existing;

                // The viewport was resized, or the resolution setting changed, underneath it.
                existing.Viewport.Dispose();
                _portalViewports.Remove(portalUid);
            }

            PortalViewport? entry = null;

            for (var index = _portalFreeViewports.Count - 1; index >= 0; index--)
            {
                var free = _portalFreeViewports[index];
                if (free.Size != size || free.RenderScale != renderScale)
                    continue;

                _portalFreeViewports.RemoveAt(index);
                entry = free;
                break;
            }

            if (entry == null)
            {
                var viewport = _clyde.CreateViewport(
                    size,
                    new TextureSampleParameters { Filter = true },
                    name: "ks-portal-view");

                viewport.RenderScale = renderScale;
                viewport.ClearColor = Color.Black;

                entry = new PortalViewport
                {
                    Viewport = viewport,
                    Size = size,
                    RenderScale = renderScale,
                };
            }

            // A reused viewport still holds some other portal's picture.
            entry.HasCapture = false;
            _portalViewports[portalUid] = entry;
            return entry;
        }

        /// <summary>
        ///     Takes the viewport of every portal not used this frame off that portal and parks it for reuse, and
        ///         disposes the ones that have sat idle for too long, or beyond what the pool can ever need.
        /// </summary>
        private void ReleaseUnusedPortalViewports()
        {
            var now = _portalTiming.RealTime;

            if (_portalViewports.Count > 0)
            {
                _portalReleaseScratch.Clear();

                foreach (var (portalUid, entry) in _portalViewports)
                {
                    if (!entry.UsedThisFrame)
                        _portalReleaseScratch.Add(portalUid);
                }

                foreach (var portalUid in _portalReleaseScratch)
                {
                    var entry = _portalViewports[portalUid];
                    _portalViewports.Remove(portalUid);
                    entry.IdleSince = now;
                    entry.HasCapture = false;
                    _portalFreeViewports.Add(entry);
                }
            }

            var maximumIdle = Math.Max(0, (_portalSystem?.MaxRendered ?? 0) - _portalViewports.Count);

            for (var index = _portalFreeViewports.Count - 1; index >= 0; index--)
            {
                var free = _portalFreeViewports[index];
                if (now - free.IdleSince < PortalViewportLifetime && _portalFreeViewports.Count <= maximumIdle)
                    continue;

                free.Viewport.Dispose();
                _portalFreeViewports.RemoveAt(index);
            }
        }

        private void ReleasePortalViewports()
        {
            foreach (var entry in _portalViewports.Values)
                entry.Viewport.Dispose();

            foreach (var entry in _portalFreeViewports)
                entry.Viewport.Dispose();

            _portalViewports.Clear();
            _portalFreeViewports.Clear();
            _portalDraws.Clear();
        }
    }
}
