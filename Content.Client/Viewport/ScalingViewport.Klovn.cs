// KS14: added in this fork
using System;
using System.Collections.Generic;
using System.Numerics;
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
        private List<Entity<KsZLevelComponent>> _mapsToIterate = [];
        private IRenderTarget? _zBlurBuffer;

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

                var isViewerMap = mapUid == topMapUid.Value;

                // clearcolor for all maps other than first is none
                _viewport!.ClearColor = null;
                // for maps below the highest, never draw FOV. on the highest map, only draw fov if we would for a non-zlevel
                _zLevelEye.DrawFov = isViewerMap && _eye.DrawFov;

                _zLevelEye.Position = new MapCoordinates(_eye.Position.Position, mapComponent.MapId);
                _zLevelEye.Scale = KsZLevelSystem.GetDepthScale(_eye.Scale, depth);
                // The viewer's own map is drawn through their real eye while they're standing on it, and
                //      through the scaled copy while they're above it mid-transit.
                _viewport.Eye = isViewerMap && depth <= 0f ? _eye : _zLevelEye;

                _viewport.Render();
                _viewport.RenderScreenOverlaysBelow(handle, this, drawBoxGlobal);

                // Never blur the map the viewer is actually on
                if (!isViewerMap && _zBlurBuffer != null)
                    _clyde.BlurRenderTarget(_viewport, _viewport.RenderTarget, _zBlurBuffer, _zLevelEye, 2.5f * mapZLevelComponent.Depth);

                handle.DrawingHandleScreen.DrawTextureRect(_viewport.RenderTarget.Texture, drawBox);
                _viewport.RenderScreenOverlaysAbove(handle, this, drawBoxGlobal);

                depth -= mapZLevelComponent.Depth;
            }

            // default clearcolor is black
            // yes if the very first frame ever is on a zlevel that isnt the deepest one, then yes one frame will unintentionally not clear
            _viewport!.ClearColor = Color.Black;
            _zLevelEye.DrawFov = _eye.DrawFov;
            return true;
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

            // Never leave an old one behind, in case this is reached without an InvalidateZLevelState first.
            InvalidateZLevelState();

            _zBlurBuffer = _clyde.CreateRenderTarget(
                size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                sampleParameters: sampleParameters);
        }
    }
}
