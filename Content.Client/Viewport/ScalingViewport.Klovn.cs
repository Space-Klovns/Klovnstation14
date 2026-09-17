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
        private IRenderTarget _zBlurBuffer = default!;

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
                !_zLevelSystem.TryGetZLevelsBelow(topMapUid.Value, _mapsToIterate) ||
                _mapsToIterate.Count == 0)
                return false;

            // TryGetZLevelsBelow doesn't include the map we're on
            var topZLevelComponent = _entityManager.GetComponent<KsZLevelComponent>(topMapUid.Value);
            _mapsToIterate.Add((topMapUid.Value, topZLevelComponent));

            // Depth is fractional and measured downwards from the viewer, not from their z-level: their own
            //      floor plane sits transitHeight of their z-level's Depth below them, and every z-level
            //      under that adds its own Depth on top. As this list is ascending, the first (bottom-most)
            //      map is the deepest, and each pass subtracts the Depth it just drew at.
            // That fractional part is what makes the world below grow continuously as you fall instead of
            //      popping one whole z-level at a time: when the viewer crosses over, their height resets to
            //      ~1 and the list loses an entry, so every remaining map keeps the depth it already had.
            var depth = GetViewerTransitHeight() * topZLevelComponent.Depth;
            for (var mapIndex = 0; mapIndex < _mapsToIterate.Count - 1; mapIndex++)
                depth += _mapsToIterate[mapIndex].Comp.Depth;

            _zLevelEye.DrawLight = _eye!.DrawLight;
            _zLevelEye.Offset = _eye.Offset;
            _zLevelEye.Rotation = _eye.Rotation;

            foreach (var (mapUid, mapZLevelComponent) in _mapsToIterate)
            {
                var isViewerMap = mapUid == topMapUid.Value;

                // clearcolor for all maps other than first is none
                _viewport!.ClearColor = null;
                // for maps below the highest, never draw FOV. on the highest map, only draw fov if we would for a non-zlevel
                _zLevelEye.DrawFov = isViewerMap && _eye.DrawFov;

                _zLevelEye.Position = new MapCoordinates(
                    _eye.Position.Position,
                    _entityManager.GetComponent<MapComponent>(mapUid).MapId
                );
                _zLevelEye.Scale = KsZLevelSystem.GetDepthScale(_eye.Scale, depth);
                // The viewer's own map is drawn through their real eye while they're standing on it, and
                //      through the scaled copy while they're above it mid-transit.
                _viewport.Eye = isViewerMap && depth <= 0f ? _eye : _zLevelEye;

                _viewport.Render();
                _viewport.RenderScreenOverlaysBelow(handle, this, drawBoxGlobal);

                // Never blur the map the viewer is actually on
                if (!isViewerMap)
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

            _zBlurBuffer = _clyde.CreateRenderTarget(
                size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                sampleParameters: sampleParameters);
        }
    }
}
