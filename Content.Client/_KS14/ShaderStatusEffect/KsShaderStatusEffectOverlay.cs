using System.Numerics;
using Content.Client._KS14.StatusEffect;
using Content.Client.Graphics;
using Content.Shared.StatusEffectNew;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KS14.ShaderStatusEffect;

/// <summary>
///     Applies the shader of every active <see cref="KsShaderStatusEffectComponent"/> effect on the local player to
///         the whole screen, chaining them in order.
/// </summary>
public sealed partial class KsShaderStatusEffectOverlay : Overlay
{
    private const string ScreenTextureParameter = "SCREEN_TEXTURE";

    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private StatusEffectsSystem _statusEffectsSystem = default!;

    [Dependency] private EntityQuery<EyeComponent> _eyeQuery = default!;
    [Dependency] private EntityQuery<KsShaderStatusEffectComponent> _shaderEffectQuery = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    /// <summary>
    ///     One unique shader instance per effect entity, dropped once that effect is no longer drawn.
    /// </summary>
    private readonly Dictionary<EntityUid, ShaderInstance> _shaderInstances = new();

    /// <summary>
    ///     Shaders to draw this frame, in order.
    /// </summary>
    private readonly List<ShaderInstance> _activeShaders = new();

    private readonly HashSet<EntityUid> _seenEffectUids = new();
    private readonly List<EntityUid> _staleEffectUids = new();

    private readonly OverlayResourceCache<CachedResources> _resources = new();

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _activeShaders.Clear();

        if (_playerManager.LocalEntity is not { } localUid ||
            !_eyeQuery.TryGetComponent(localUid, out var eyeComponent) ||
            args.Viewport.Eye != eyeComponent.Eye)
            return false;

        _seenEffectUids.Clear();
        var curTime = _gameTiming.CurTime;

        foreach (var effect in _statusEffectsSystem.EnumerateStatusEffects(localUid, _shaderEffectQuery))
        {
            var statusEffectComponent = effect.Comp1;
            var shaderEffectComponent = effect.Comp2;

            // Delayed and not started yet.
            if (statusEffectComponent.StartEffectTime > curTime)
                continue;

            if (!_shaderInstances.TryGetValue(effect.Owner, out var shaderInstance))
            {
                shaderInstance = _prototypeManager.Index(shaderEffectComponent.Shader).InstanceUnique();
                foreach (var (name, parameter) in shaderEffectComponent.Parameters)
                    parameter.Apply(shaderInstance, name);

                _shaderInstances[effect.Owner] = shaderInstance;
            }

            if (shaderEffectComponent.TimeLeftParameter is { } timeLeftParameter)
                shaderInstance.SetParameter(timeLeftParameter, KsStatusEffectTimeLeft.GetRatio(statusEffectComponent, curTime));

            _seenEffectUids.Add(effect.Owner);
            _activeShaders.Add(shaderInstance);
        }

        PruneShaders();
        return _activeShaders.Count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null || _activeShaders.Count == 0)
            return;

        var worldHandle = args.WorldHandle;
        Texture sourceTexture = ScreenTexture;

        // Every shader but the last renders into a ping-pong target that the next one samples.
        if (_activeShaders.Count > 1)
        {
            var resources = _resources.GetForViewport(args.Viewport, static _ => new CachedResources());
            resources.EnsureSize(_clyde, ScreenTexture.Size);

            var targetBounds = Box2.FromDimensions(Vector2.Zero, ScreenTexture.Size);
            for (var i = 0; i < _activeShaders.Count - 1; i++)
            {
                var shaderInstance = _activeShaders[i];
                var renderTarget = resources.PingPong[i % 2]!;

                shaderInstance.SetParameter(ScreenTextureParameter, sourceTexture);
                worldHandle.RenderInRenderTarget(renderTarget,
                    () =>
                    {
                        // Render targets draw in pixel space, but keep the current model transform.
                        // Untested with 2+ effects in-game: if the chained result comes out upside down, the
                        //      intermediate pass is flipped relative to the screen texture - fix it by inverting Y
                        //      here, e.g. SetTransform(Matrix3x2.CreateScale(1f, -1f) * Matrix3x2.CreateTranslation(0f, targetBounds.Height)).
                        worldHandle.SetTransform(Matrix3x2.CreateScale(1f, -1f) * Matrix3x2.CreateTranslation(0f, targetBounds.Height));
                        worldHandle.UseShader(shaderInstance);
                        worldHandle.DrawRect(targetBounds, Color.White);
                        worldHandle.UseShader(null);
                    },
                    Color.Transparent);

                sourceTexture = renderTarget.Texture;
            }
        }

        var lastShaderInstance = _activeShaders[^1];
        lastShaderInstance.SetParameter(ScreenTextureParameter, sourceTexture);
        worldHandle.UseShader(lastShaderInstance);
        worldHandle.DrawRect(args.WorldBounds, Color.White);
        worldHandle.UseShader(null);
    }

    /// <summary>
    ///     Disposes every cached shader instance.
    /// </summary>
    public void ClearShaders()
    {
        foreach (var shaderInstance in _shaderInstances.Values)
            shaderInstance.Dispose();

        _shaderInstances.Clear();
        _activeShaders.Clear();
    }

    protected override void DisposeBehavior()
    {
        ClearShaders();
        _resources.Dispose();

        base.DisposeBehavior();
    }

    /// <summary>
    ///     Disposes the shader instances of effects that were not seen this frame.
    /// </summary>
    private void PruneShaders()
    {
        _staleEffectUids.Clear();
        foreach (var effectUid in _shaderInstances.Keys)
        {
            if (!_seenEffectUids.Contains(effectUid))
                _staleEffectUids.Add(effectUid);
        }

        foreach (var effectUid in _staleEffectUids)
        {
            _shaderInstances.Remove(effectUid, out var shaderInstance);
            shaderInstance?.Dispose();
        }
    }

    private sealed class CachedResources : IDisposable
    {
        public readonly IRenderTexture?[] PingPong = new IRenderTexture?[2];

        public void EnsureSize(IClyde clyde, Vector2i size)
        {
            for (var i = 0; i < PingPong.Length; i++)
            {
                if (PingPong[i]?.Size == size)
                    continue;

                PingPong[i]?.Dispose();
                PingPong[i] = clyde.CreateRenderTarget(
                    size,
                    new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                    name: $"{nameof(KsShaderStatusEffectOverlay)}-{i}");
            }
        }

        public void Dispose()
        {
            foreach (var renderTarget in PingPong)
                renderTarget?.Dispose();
        }
    }
}
