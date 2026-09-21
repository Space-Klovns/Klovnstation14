// KS14: added in this fork
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client._KS14.ZLevel;
using Content.Client._KS14.ZLevel.Light;
using Content.Client._KS14.ZLevel.Transit;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Physics;
using Content.Shared._KS14.ZLevel.Transit;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Client.Viewport
{
    /// <summary>
    ///     Z-level rendering: everything below the viewer is drawn as its own full pass into the same viewport,
    ///         back to front, each through a copy of the viewer's eye scaled down by how far below them it sits.
    /// </summary>
    public sealed partial class ScalingViewport
    {
        [Dependency] private IPlayerManager _playerManager = default!;

        /// <summary>
        ///     A stand-in for the viewer's own eye, repositioned and rescaled once per z-level pass.
        ///     FOV and lights are turned back on per pass; they are off here only as the default.
        /// </summary>
        private Robust.Shared.Graphics.Eye _zLevelEye = new Robust.Shared.Graphics.Eye()
        {
            DrawFov = false,
            DrawLight = false
        };

        private MapSystem _mapSystem = default!;
        private KsZLevelSystem _zLevelSystem = null!;
        private KsZLevelGapSystem _gapSystem = null!;
        private KsZLevelLightBufferSystem _lightBufferSystem = null!;
        private List<Entity<KsZLevelComponent>> _mapsToIterate = [];
        private List<Entity<KsZLevelGapComponent>> _gapsToIterate = [];
        private IRenderTarget? _zBlurBuffer;

        /// <summary>
        ///     Whether a grid crossing a gap is drawn for viewers on other z-levels.
        /// </summary>
        /// <remarks>
        ///     One extra full pass per grid in flight, for everyone above it, so it is worth being able to
        ///         turn off. A viewer standing on a gap map is unaffected either way - their own map is
        ///         always drawn.
        /// </remarks>
        private bool _drawGapLevels = true;

        /// <summary>
        ///     How much of a crossing is left when the grid making it starts to fade in for the z-level it
        ///         is arriving at, as a fraction of the whole gap.
        /// </summary>
        /// <remarks>
        ///     Deliberately far later than the fade on a falling entity, and not the same number. A faller
        ///         is one small sprite that wants picking out of the floor early; a gap pass is a
        ///         full-screen composite over the z-level below it, so a platform visible for the whole of
        ///         its descent means everyone under the shaft spends the ride looking at the underside of a
        ///         lift rather than at the room they are in.
        ///     Reaching exactly 1 at the end of the crossing is what makes the hand-off seamless: the
        ///         instant the grid lands it stops being drawn by this pass and starts being drawn by the
        ///         ordinary one for its new z-level, at full opacity either way.
        /// </remarks>
        private const float GapFadeInProgress = 0.1f;

        /// <summary>
        ///     Held so the subscription can be taken back off again. A cvar's subscriber list is rooted for
        ///         the life of the process, so a handler closing over a viewport keeps that viewport - and
        ///         the render targets hanging off it - alive forever once the control is thrown away.
        /// </summary>
        private Action<bool>? _gapLevelsCVarHandler;

        /// <summary>
        ///     The light map and floor of each z-level that lights another, taken before the drawing starts.
        /// </summary>
        /// <remarks>
        ///     Owned here rather than by the overlay that uses them because this is the only thing that can
        ///         drive the extra render passes, and the only thing that knows when the viewport they are
        ///         sized against has gone.
        /// </remarks>
        private readonly Dictionary<MapId, KsZLevelLightCapture> _zLightCaptures = [];
        private readonly List<MapId> _staleCaptures = [];

        /// <summary>
        ///     How much wider than the visible light target this viewport's captures have to be taken, as
        ///         measured by the overlay that composites them.
        /// </summary>
        /// <remarks>
        ///     Held per viewport rather than read straight off the system, because the system's copy is
        ///         whatever the last control to draw put there - and a surveillance camera's light target is
        ///         a different size to the main one, so its ratio is a different number.
        /// </remarks>
        private Vector2 _captureOversize = Vector2.One;

        /// <summary>
        ///     Draws every z-level at or below the viewer, deepest first.
        /// </summary>
        /// <returns>
        ///     Whether the viewport was drawn. False means the viewer is not on a z-level stack with anything
        ///         below them, and <see cref="Draw"/> should fall back to its ordinary single pass.
        /// </returns>
        private bool TryDrawZLevels(IRenderHandle handle, UIBox2i drawBox, UIBox2i drawBoxGlobal)
        {
            _mapsToIterate.Clear();

            if (!_mapSystem.TryGetMap(_eye?.Position.MapId, out var topMapUid) ||
                !_zLevelSystem.TryGetZLevelsBelow(topMapUid.Value, _mapsToIterate))
                return false;

            var viewerTransitHeight = GetViewerTransitHeight();

            // Nothing below to draw, and a viewer standing on their own floor, is exactly what the ordinary
            //      single pass already does - and cheaper. Mid-transit there is still their own z-level to
            //      scale down, and KsZLevelTransitSpriteSystem compensates sprites against that scale whether
            //      this ran or not, so bailing while they are off the ground leaves the two disagreeing.
            // The third case is standing on the bottom of a stack with a lit z-level overhead: nothing below,
            //      feet on the floor, and the single pass would do. Except that this loop is the only thing
            //      that ever renders the z-level above, so bailing here is what made light from above arrive
            //      during a fall and then stop the moment it ended.
            // The fourth is the same shape: a grid arriving through the gap overhead is drawn by this loop
            //      and by nothing else, so bailing leaves an elevator - and anything riding it - popping
            //      into existence at the moment it lands instead of easing in over the end of its descent.
            if (_mapsToIterate.Count == 0 &&
                viewerTransitHeight <= 0f &&
                !WantsLightFromAbove(topMapUid.Value) &&
                !WantsGapPass(topMapUid.Value))
                return false;

            // TryGetZLevelsBelow doesn't include the map we're on
            if (!_entityManager.TryGetComponent<KsZLevelComponent>(topMapUid.Value, out var topZLevelComponent))
                return false;

            _mapsToIterate.Add((topMapUid.Value, topZLevelComponent));

            // Depth is fractional and measured downwards from the viewer, not from their z-level: their own
            //      floor plane sits transitHeight of their z-level's Depth below them, and the deepest map
            //      in the list is however far under that. As this list is ascending, the first (bottom-most)
            //      map is the deepest, and each pass subtracts the Depth it just drew at.
            // That fractional part is what makes the world below grow continuously as you fall instead of
            //      popping one whole z-level at a time: when the viewer crosses over, their height resets to
            //      ~1 and the list loses an entry, so every remaining map keeps the depth it already had.
            // Summed through TryGetDepthBelow rather than by adding up the list, because that is the one
            //      definition of the distance between two z-levels - and it is what resolves a viewer who is
            //      standing on a gap map to however far up that gap they have got.
            var depth = viewerTransitHeight * topZLevelComponent.Depth;
            if (_mapsToIterate.Count > 1 &&
                _zLevelSystem.TryGetDepthBelow(topMapUid.Value, _mapsToIterate[0].Owner, out var stackDepth))
                depth += stackDepth;

            _zLevelEye.DrawLight = _eye!.DrawLight;
            _zLevelEye.Offset = _eye.Offset;
            _zLevelEye.Rotation = _eye.Rotation;

            // Everything from here to the finally is drawn with the captures in scope: the capture passes
            //      themselves want them, so that a z-level is already lit from above before it lights the one
            //      below it, and the drawing passes want them for the same reason.
            _lightBufferSystem.SetActiveCaptures(_zLightCaptures);

            var captured = _lightBufferSystem.WantsCaptures;

            try
            {
                if (captured)
                    CaptureZLevelLight(handle, (topMapUid.Value, topZLevelComponent), viewerTransitHeight);

                DrawZLevelPasses(handle, drawBox, drawBoxGlobal, topMapUid.Value, depth, clearFirstPass: captured);

                // Taken back off the system while this viewport is still the one that drew, so that the next
                //      frame's captures are sized against this viewport's light target and not some other
                //      control's.
                _captureOversize = _lightBufferSystem.CaptureOversize;
            }
            finally
            {
                // A pointer left behind here would have the next viewport lighting itself from this one's
                //      z-levels, at this one's scale.
                _lightBufferSystem.SetActiveCaptures(null);
            }

            // default clearcolor is black
            // yes if the very first frame ever is on a zlevel that isnt the deepest one, then yes one frame will unintentionally not clear
            _viewport!.ClearColor = Color.Black;
            _zLevelEye.DrawFov = _eye.DrawFov;
            return true;
        }

        /// <summary>
        ///     Draws the stack back to front, deepest first, accumulating into the one viewport target.
        /// </summary>
        private void DrawZLevelPasses(
            IRenderHandle handle,
            UIBox2i drawBox,
            UIBox2i drawBoxGlobal,
            EntityUid topMapUid,
            float depth,
            bool clearFirstPass)
        {
            // Only reached with an eye; taken once so the passes below read as the original single one did.
            var eye = _eye!;

            foreach (var (mapUid, mapZLevelComponent) in _mapsToIterate)
            {
                // A stack is rebuilt wholesale from network state, so a member can briefly be an entity that is
                //      not a map, or one that has since been deleted. Skipping the pass costs a frame of that
                //      z-level; throwing out of here takes the entire UI down with it, every frame.
                if (!_entityManager.TryGetComponent<MapComponent>(mapUid, out var mapComponent))
                {
                    depth -= mapZLevelComponent.Depth;
                    continue;
                }

                var isViewerMap = mapUid == topMapUid;

                // clearcolor for all maps other than first is none
                // The one exception is a frame that captured: those passes left their own output sitting in
                //      the target, and the deepest pass here would otherwise be composited over a picture of
                //      some other z-level rather than over last frame's near-identical one.
                _viewport!.ClearColor = clearFirstPass ? Color.Black : null;
                clearFirstPass = false;
                // for maps below the highest, never draw FOV. on the highest map, only draw fov if we would for a non-zlevel
                _zLevelEye.DrawFov = isViewerMap && eye.DrawFov;

                _zLevelEye.Position = new MapCoordinates(eye.Position.Position, mapComponent.MapId);
                _zLevelEye.Scale = KsZLevelSystem.GetDepthScale(eye.Scale, depth);
                // The viewer's own map is drawn through their real eye while they're standing on it, and
                //      through the scaled copy while they're above it mid-transit.
                _viewport.Eye = isViewerMap && depth <= 0f ? eye : _zLevelEye;

                _viewport.Render();
                _viewport.RenderScreenOverlaysBelow(handle, this, drawBoxGlobal);

                // Never blur the map the viewer is actually on
                if (!isViewerMap && _zBlurBuffer != null)
                    _clyde.BlurRenderTarget(_viewport, _viewport.RenderTarget, _zBlurBuffer, _zLevelEye, 2.5f * mapZLevelComponent.Depth);

                handle.DrawingHandleScreen.DrawTextureRect(_viewport.RenderTarget.Texture, drawBox);
                _viewport.RenderScreenOverlaysAbove(handle, this, drawBoxGlobal);

                // Anything crossing the gap above this z-level is drawn after it and before the next one up,
                //      so an elevator between two floors is seen where it actually is rather than vanishing
                //      for the length of its ride.
                // Run for the viewer's own map too, where it is the gap directly overhead: a lift coming
                //      down to the floor you are standing on lives there for the whole of its descent, and
                //      skipping it is what made one appear out of nothing the instant it arrived.
                DrawGapPasses(handle, drawBox, drawBoxGlobal, mapUid, depth);

                depth -= mapZLevelComponent.Depth;
            }
        }

        /// <summary>
        ///     Draws whatever is partway up the gap above a z-level, nearest the floor first.
        /// </summary>
        /// <remarks>
        ///     A gap map is on no stack, so nothing else in this file would ever reach one. This draws it for
        ///         viewers looking at it from elsewhere; a viewer standing on one is drawn by the ordinary
        ///         pass for their own map, which is the whole point of putting them on a map.
        ///     The depth here can be negative, which no ordinary pass ever is. A gap above the viewer really
        ///         is nearer the camera than their own floor, and drawing it scaled up accordingly is what
        ///         makes a lift descending towards you grow smoothly until it lands at exactly the scale
        ///         everything else on your floor is drawn at. Only a gap is drawn this way - a whole z-level
        ///         overhead would be a ceiling, and would hide everything.
        /// </remarks>
        private void DrawGapPasses(
            IRenderHandle handle,
            UIBox2i drawBox,
            UIBox2i drawBoxGlobal,
            EntityUid anchorUid,
            float anchorDepth)
        {
            if (!_drawGapLevels)
                return;

            var eye = _eye!;

            _gapSystem.GetGapsAnchoredTo(anchorUid, _gapsToIterate);

            foreach (var (gapUid, gapComponent) in _gapsToIterate)
            {
                // The viewer's own gap is drawn by the ordinary pass, through their real eye. Drawing it
                //      again here would put a second copy of the lift over the one they are standing on.
                if (!_entityManager.TryGetComponent<MapComponent>(gapUid, out var mapComponent) ||
                    mapComponent.MapId == eye.Position.MapId)
                    continue;

                // A gap sits above the z-level it is anchored to, so it is that much nearer the viewer.
                var gapDepth = anchorDepth - gapComponent.Progress * gapComponent.TotalDepth;

                // Coming down onto the viewer's own floor, a platform descends through a ceiling that is
                //      never rendered, so it fades in over the last stretch rather than appearing out of
                //      nothing. Seen from a z-level above, it is descending into a shaft already in view,
                //      so it stays opaque.
                // Measured on how near the platform is to this floor plane rather than on which way it is
                //      travelling, so the two directions are the same rule: one arriving fades in over the
                //      end of its descent, and one setting off upwards fades out over the start of its
                //      climb.
                var modulate = Color.White;
                if (anchorDepth <= 0f)
                {
                    var nearness = (GapFadeInProgress - gapComponent.Progress) / GapFadeInProgress;
                    var alpha = Math.Clamp(nearness, 0f, 1f);

                    // Nothing to composite, and a full pass is the most expensive thing in this loop - so
                    //      for the nine tenths of every crossing that are invisible, skip it outright.
                    if (alpha <= 0f)
                        continue;

                    modulate = Color.White.WithAlpha(alpha);
                }

                _viewport!.ClearColor = null;
                _zLevelEye.DrawFov = false;
                _zLevelEye.Position = new MapCoordinates(eye.Position.Position, mapComponent.MapId);
                _zLevelEye.Scale = KsZLevelSystem.GetDepthScale(eye.Scale, gapDepth);
                _viewport.Eye = _zLevelEye;

                _viewport.Render();
                _viewport.RenderScreenOverlaysBelow(handle, this, drawBoxGlobal);

                // Distance blur only, and only for a gap below. One overhead is nearer than the viewer's own
                //      floor, so there is nothing to blur it by - and a negative radius is not a thing.
                if (_zBlurBuffer != null && gapDepth > 0f)
                    _clyde.BlurRenderTarget(_viewport, _viewport.RenderTarget, _zBlurBuffer, _zLevelEye, 2.5f * gapDepth);

                handle.DrawingHandleScreen.DrawTextureRect(_viewport.RenderTarget.Texture, drawBox, modulate);
                _viewport.RenderScreenOverlaysAbove(handle, this, drawBoxGlobal);
            }
        }

        /// <summary>
        ///     Releases the render targets the z-level passes blur through and capture into.
        /// </summary>
        /// <remarks>
        ///     A control is not only regenerated, it is also thrown away - closing a surveillance camera window
        ///         is the ordinary way - and nothing upstream releases anything when that happens. Regeneration
        ///         alone would leave every closed viewport holding a full-size blur buffer plus two capture
        ///         targets per z-level it had drawn, for the rest of the session.
        /// </remarks>
        protected override void Dispose(bool disposing)
        {
            InvalidateZLevelState();

            if (_gapLevelsCVarHandler != null)
            {
                IoCManager.Resolve<IConfigurationManager>()
                    .UnsubValueChanged(KsCCVars.ZLevelDrawGapLevels, _gapLevelsCVarHandler);

                _gapLevelsCVarHandler = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>
        ///     Releases the render target the z-level passes blur through.
        /// </summary>
        /// <remarks>
        ///     Tied to the viewport's lifetime because the buffer is sized to match it. Without this, every
        ///         viewport regeneration - a resize, a stretch mode or render scale change - stranded a
        ///         full-size GPU render target for the rest of the session.
        /// </remarks>
        private void InvalidateZLevelState()
        {
            _zBlurBuffer?.Dispose();
            _zBlurBuffer = null;

            foreach (var capture in _zLightCaptures.Values)
                capture.Dispose();

            _zLightCaptures.Clear();
        }

        /// <summary>
        ///     Whether this draw has to run the pass loop purely to capture the z-level above the viewer.
        /// </summary>
        /// <remarks>
        ///     The MapComponent check catches a stack member that has since been deleted outright - a full
        ///         state reset on reconnect, most likely - because the stack is replicated as net entities and
        ///         rebuilt wholesale, and a node left pointing at a dead uid still answers yes.
        ///     It does not catch the z-level above merely having left PVS, and cannot: leaving PVS detaches
        ///         rather than deletes, and a map entity has no parent to be detached from, so its
        ///         MapComponent outlives its contents. That case is what
        ///         <see cref="KsZLevelLightBufferSystem.WantsLightFromAbove"/> is for.
        /// </remarks>
        private bool WantsLightFromAbove(EntityUid topMapUid)
        {
            return _lightBufferSystem.WantsLightFromAbove &&
                   _zLevelSystem.TryGetZLevelAbove(topMapUid, out var aboveEntity) &&
                   _entityManager.HasComponent<MapComponent>(aboveEntity.Value.Owner);
        }

        /// <summary>
        ///     Whether anything crossing the gap over this z-level is near enough to it to be worth a pass.
        /// </summary>
        /// <remarks>
        ///     Only asked about the viewer's own z-level, and only to decide whether the layered draw has to
        ///         run at all when nothing else needs it - somebody on the bottom floor of a stack with an
        ///         unlit ceiling has nothing else to layer. Since a crossing is invisible for all but the
        ///         last <see cref="GapFadeInProgress"/> of itself, asking merely whether one exists would
        ///         put that viewer on the expensive path for the whole of every ride and draw nothing extra
        ///         for nine tenths of it.
        /// </remarks>
        private bool WantsGapPass(EntityUid anchorUid)
        {
            if (!_drawGapLevels)
                return false;

            _gapSystem.GetGapsAnchoredTo(anchorUid, _gapsToIterate);

            foreach (var (_, gapComponent) in _gapsToIterate)
            {
                if (gapComponent.Progress < GapFadeInProgress)
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     Renders every z-level that lights another, purely to take its light map and its floor away
        ///         before the drawing starts.
        /// </summary>
        /// <remarks>
        ///     The engine clears the light target at the top of every pass, before any overlay gets to look at
        ///         it, so a pass can never see the one before it. Copying it out in between is the only way a
        ///         z-level can be lit by the one above it.
        ///     Walked top-down so that each capture is itself already lit from above, which is what lets the
        ///         light carry further than one z-level.
        /// </remarks>
        private void CaptureZLevelLight(IRenderHandle handle, Entity<KsZLevelComponent> topZLevel, float viewerTransitHeight)
        {
            ReleaseUnusedCaptures();

            // The viewer's own z-level sits this far below where they are looking from, and every capture is
            //      measured from there.
            var viewerDepth = viewerTransitHeight * topZLevel.Comp.Depth;

            // Nothing renders the z-level above the viewer - the stack is only ever drawn downwards - so if
            //      light is to fall onto them from it, this is the only pass that will ever exist for it.
            //      The z-level it lights is the viewer's own, hence their own depth rather than its.
            if (WantsLightFromAbove(topZLevel.Owner) &&
                _zLevelSystem.TryGetZLevelAbove(topZLevel.Owner, out var aboveEntity))
                CaptureZLevel(handle, aboveEntity.Value, viewerDepth);

            // Top-down, stopping before the deepest: nothing is drawn under it for its light to fall on.
            var depth = viewerDepth;
            for (var index = _mapsToIterate.Count - 1; index >= 1; index--)
            {
                // Stepping down into a z-level crosses that z-level's own Depth, matching the draw loop -
                //      and taking the step before the capture rather than after is what hands it the depth
                //      of the z-level it is about to light rather than its own.
                depth += _mapsToIterate[index - 1].Comp.Depth;

                CaptureZLevel(handle, _mapsToIterate[index], depth);
            }
        }

        /// <param name="consumerDepth">
        ///     How far below the viewer the z-level this capture is going to light sits - not how far down
        ///         this one is.
        /// </param>
        private void CaptureZLevel(IRenderHandle handle, Entity<KsZLevelComponent> zLevel, float consumerDepth)
        {
            if (!_entityManager.TryGetComponent<MapComponent>(zLevel.Owner, out var mapComponent))
                return;

            // Never the viewer's FOV: this z-level is not the one they are standing on, and carving their
            //      line of sight out of it would cut shadows into light that was never theirs to block.
            _zLevelEye.DrawFov = false;
            _zLevelEye.Position = new MapCoordinates(_eye!.Position.Position, mapComponent.MapId);

            // Rendered through the eye of the z-level it is about to light rather than its own, widened by
            //      the light target's skirt on top.
            // Its own scale would buy nothing: light falls straight down, so a capture is projected onto the
            //      z-level below at the same world coordinates whatever scale it was drawn at, and all its
            //      own scale decides is how much of the world it covers. Drawn at its own scale it covers
            //      less than the pass reading it can see, because that pass is further away and sees wider -
            //      and the strip of screen past the edge of the capture is then lit by nothing at all.
            _zLevelEye.Scale =
                KsZLevelSystem.GetDepthScale(_eye.Scale, consumerDepth) / _captureOversize;

            _viewport!.Eye = _zLevelEye;

            // Cleared, unlike the drawing passes: the colour image is about to be used as a mask, and it can
            //      only say where this z-level is solid if nothing else is left in it.
            _viewport.ClearColor = Color.Transparent;
            _viewport.Render();

            var capture = EnsureCapture(mapComponent.MapId);
            capture.WorldBounds = GetEyeWorldBounds(_zLevelEye);

            CopyIntoCapture(handle, _viewport.LightRenderTarget.Texture, capture.Light, capture.WorldBounds);
            CopyIntoCapture(handle, _viewport.RenderTarget.Texture, capture.Colour, capture.WorldBounds);
        }

        /// <summary>
        ///     Copies a pass's output into a capture of the same size, over the world area it covers.
        /// </summary>
        /// <remarks>
        ///     Done in world space rather than as a flat pixel blit so that it is the exact inverse of how the
        ///         overlay reads it back out, and so neither has to reason about which way up a render target
        ///         is.
        /// </remarks>
        private void CopyIntoCapture(IRenderHandle handle, Texture source, IRenderTexture destination, Box2Rotated bounds)
        {
            var worldHandle = handle.DrawingHandleWorld;

            // A capture is at the light target's resolution, not the viewport's, so the eye scale it is drawn
            //      through has to be corrected by the difference. The same correction every light overlay makes.
            var captureScale = destination.Size / (Vector2)_viewport!.Size;
            var scale = _viewport.RenderScale / (Vector2.One / captureScale);
            var matrix = destination.GetWorldToLocalMatrix(_zLevelEye, scale);

            worldHandle.RenderInRenderTarget(
                destination,
                () =>
                {
                    worldHandle.SetTransform(matrix);
                    worldHandle.DrawTextureRect(source, bounds);
                },
                Color.Transparent
            );
        }

        /// <summary>
        ///     The world area an eye covers in this viewport, which is what places a capture correctly in a
        ///         pass drawn at a different depth scale.
        /// </summary>
        private Box2Rotated GetEyeWorldBounds(Robust.Shared.Graphics.Eye eye)
        {
            var eyePosition = eye.Position.Position + eye.Offset;
            var worldSize = (Vector2)_viewport!.Size
                / (EyeManager.PixelsPerMeter * _viewport.RenderScale * eye.Scale);

            return new Box2Rotated(Box2.CenteredAround(eyePosition, worldSize), eye.Rotation, eyePosition);
        }

        private KsZLevelLightCapture EnsureCapture(MapId mapId)
        {
            var size = _viewport!.LightRenderTarget.Size;

            if (_zLightCaptures.TryGetValue(mapId, out var capture) && capture.Light.Size == size)
                return capture;

            // Either new, or the light resolution changed underneath it.
            capture?.Dispose();

            capture = new KsZLevelLightCapture
            {
                // Matches the engine's own light target format, so the high dynamic range a bright light
                //      lands in the buffer with survives being copied out of it.
                Light = _clyde.CreateLightRenderTarget(size, $"ks-zlevel-light-{mapId}"),

                // Needs a real alpha channel, which the light format has none of - the alpha is the whole
                //      point of keeping the colour image at all.
                Colour = _clyde.CreateRenderTarget(
                    size,
                    new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                    name: $"ks-zlevel-floor-{mapId}"),
            };

            _zLightCaptures[mapId] = capture;
            return capture;
        }

        /// <summary>
        ///     Drops captures for z-levels that are no longer part of the stack being drawn.
        /// </summary>
        private void ReleaseUnusedCaptures()
        {
            if (_zLightCaptures.Count == 0)
                return;

            _staleCaptures.Clear();

            foreach (var (mapId, _) in _zLightCaptures)
            {
                if (!_mapSystem.TryGetMap(mapId, out var mapUid) || !_entityManager.HasComponent<KsZLevelComponent>(mapUid))
                    _staleCaptures.Add(mapId);
            }

            foreach (var mapId in _staleCaptures)
            {
                _zLightCaptures[mapId].Dispose();
                _zLightCaptures.Remove(mapId);
            }
        }

        /// <summary>
        ///     Points the viewport back at the viewer's own eye if a z-level pass currently has it aimed at a
        ///         different map, and hands back whatever it displaced so it can be put straight back.
        /// </summary>
        /// <remarks>
        ///     Every pass aims the viewport's eye at its own z-level for the length of its render, and world
        ///         overlays are drawn inside that render. Anything converting screen coordinates through this
        ///         control mid-pass therefore gets an answer on the wrong map: the placement preview asks for
        ///         the mouse position and compares it against the grid its drag started on, which is on the
        ///         player's map, and the mismatch is an error log per overlay per pass per frame.
        ///     Only a differing map is corrected. A pass on the viewer's own z-level may legitimately carry a
        ///         different scale while they are mid-transit, and screen conversions should use that scale
        ///         because it is what was actually drawn.
        ///     Only the screen-to-map direction is corrected, too. A screen position is the player's, so it has
        ///         to resolve on the player's map - but a world-to-screen conversion is placing something whose
        ///         map is already known, and overlays that do it inside a pass (PopupOverlay, MapTextOverlay)
        ///         filter on that pass's MapId first and want the scale that pass was drawn at.
        /// </remarks>
        private IEye? SwapToViewerEyeIfOffMap()
        {
            if (_eye == null ||
                _viewport?.Eye is not { } passEye ||
                passEye.Position.MapId == _eye.Position.MapId)
                return null;

            _viewport.Eye = _eye;
            return passEye;
        }

        private void RestorePassEye(IEye? passEye)
        {
            if (passEye != null && _viewport != null)
                _viewport.Eye = passEye;
        }

        /// <summary>
        ///     How far above their own z-level's floor plane the viewer currently is, 0 to 1.
        /// </summary>
        private float GetViewerTransitHeight()
        {
            // The MapID check stops a viewport that isn't the player's own - a surveillance camera, say - from
            //      inheriting the player's transit height.
            if (_eye == null ||
                _playerManager.LocalEntity is not { } localUid ||
                !_entityManager.TryGetComponent<TransformComponent>(localUid, out var transformComponent) ||
                transformComponent.MapID != _eye.Position.MapId)
                return 0f;

            // Only a fall. Someone riding a grid across a gap is standing on a gap map, which is a place in
            //      its own right rather than a height above one, so their altitude comes out of the depth to
            //      the z-levels below them like everybody else's.
            if (_entityManager.TryGetComponent<KsZLevelTransitComponent>(localUid, out var transitComponent))
                return Math.Clamp(transitComponent.Height, 0f, 1f);

            return 0f;
        }

        /// <summary>
        ///     Rebuilds the state the z-level passes need, alongside the viewport itself.
        /// </summary>
        /// <remarks>
        ///     The systems are resolved lazily here rather than in the constructor because a ScalingViewport can
        ///         be constructed before the entity systems exist.
        /// </remarks>
        private void RegenerateZLevelState(Vector2i size, TextureSampleParameters sampleParameters)
        {
            _mapSystem ??= _entityManager.System<MapSystem>();
            _zLevelSystem ??= _entityManager.System<KsZLevelSystem>();
            _gapSystem ??= _entityManager.System<KsZLevelGapSystem>();
            _lightBufferSystem ??= _entityManager.System<KsZLevelLightBufferSystem>();

            // Subscribed here rather than read per frame, and only once however often this is regenerated.
            if (_gapLevelsCVarHandler == null)
            {
                _gapLevelsCVarHandler = value => _drawGapLevels = value;
                IoCManager.Resolve<IConfigurationManager>()
                    .OnValueChanged(KsCCVars.ZLevelDrawGapLevels, _gapLevelsCVarHandler, invokeImmediately: true);
            }

            // Never leave an old one behind, in case this is reached without an InvalidateZLevelState first.
            InvalidateZLevelState();

            _zBlurBuffer = _clyde.CreateRenderTarget(
                size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                sampleParameters: sampleParameters);
        }
    }
}
