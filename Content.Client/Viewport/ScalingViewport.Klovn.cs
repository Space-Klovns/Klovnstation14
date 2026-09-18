// KS14: added in this fork
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client._KS14.ZLevel.Light;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Physics;
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
        private KsZLevelLightBufferSystem _lightBufferSystem = null!;
        private List<Entity<KsZLevelComponent>> _mapsToIterate = [];
        private IRenderTarget? _zBlurBuffer;

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
            if (_mapsToIterate.Count == 0 && viewerTransitHeight <= 0f)
                return false;

            // TryGetZLevelsBelow doesn't include the map we're on
            if (!_entityManager.TryGetComponent<KsZLevelComponent>(topMapUid.Value, out var topZLevelComponent))
                return false;

            _mapsToIterate.Add((topMapUid.Value, topZLevelComponent));

            // Depth is fractional and measured downwards from the viewer, not from their z-level: their own
            //      floor plane sits transitHeight of their z-level's Depth below them, and every z-level
            //      under that adds its own Depth on top. As this list is ascending, the first (bottom-most)
            //      map is the deepest, and each pass subtracts the Depth it just drew at.
            // That fractional part is what makes the world below grow continuously as you fall instead of
            //      popping one whole z-level at a time: when the viewer crosses over, their height resets to
            //      ~1 and the list loses an entry, so every remaining map keeps the depth it already had.
            var depth = viewerTransitHeight * topZLevelComponent.Depth;
            for (var mapIndex = 0; mapIndex < _mapsToIterate.Count - 1; mapIndex++)
                depth += _mapsToIterate[mapIndex].Comp.Depth;

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

                depth -= mapZLevelComponent.Depth;
            }
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
            if (_zLevelSystem.TryGetZLevelAbove(topZLevel.Owner, out var aboveEntity))
                CaptureZLevel(handle, aboveEntity.Value, viewerDepth - topZLevel.Comp.Depth);

            // Top-down, stopping before the deepest: nothing is drawn under it for its light to fall on.
            var depth = viewerDepth;
            for (var index = _mapsToIterate.Count - 1; index >= 1; index--)
            {
                CaptureZLevel(handle, _mapsToIterate[index], depth);

                // Stepping down into a z-level crosses that z-level's own Depth, matching the draw loop.
                depth += _mapsToIterate[index - 1].Comp.Depth;
            }
        }

        private void CaptureZLevel(IRenderHandle handle, Entity<KsZLevelComponent> zLevel, float depth)
        {
            if (!_entityManager.TryGetComponent<MapComponent>(zLevel.Owner, out var mapComponent))
                return;

            // Never the viewer's FOV: this z-level is not the one they are standing on, and carving their
            //      line of sight out of it would cut shadows into light that was never theirs to block.
            _zLevelEye.DrawFov = false;
            _zLevelEye.Position = new MapCoordinates(_eye!.Position.Position, mapComponent.MapId);
            _zLevelEye.Scale = KsZLevelSystem.GetDepthScale(_eye.Scale, depth);

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
                !_entityManager.TryGetComponent<KsZLevelTransitComponent>(localUid, out var transitComponent) ||
                !_entityManager.TryGetComponent<TransformComponent>(localUid, out var transformComponent) ||
                transformComponent.MapID != _eye.Position.MapId)
                return 0f;

            return Math.Clamp(transitComponent.Height, 0f, 1f);
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
            _lightBufferSystem ??= _entityManager.System<KsZLevelLightBufferSystem>();

            // Never leave an old one behind, in case this is reached without an InvalidateZLevelState first.
            InvalidateZLevelState();

            _zBlurBuffer = _clyde.CreateRenderTarget(
                size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                sampleParameters: sampleParameters);
        }
    }
}
