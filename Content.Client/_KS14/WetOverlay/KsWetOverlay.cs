using System.Numerics;
using Content.Client.Light.EntitySystems;
using Content.Client.Weather;
using Content.Shared._KS14.WetOverlay;
using Content.Shared.Light.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._KS14.WetOverlay;

public sealed class KsWetOverlay(ShaderInstance shader) : Overlay
{
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly EntityManager _entityManager = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly MapSystem _mapSystem = default!;
    [Dependency] private readonly WeatherSystem _weatherSystem = default!;

    [Dependency] private readonly EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private readonly EntityQuery<KsWetMapComponent> _wetMapQuery = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader = shader;

    public const int HardDropletCap = 96;
    /// <summary>
    ///     Droplets hitting you per second.
    /// </summary>
    public const float DropletExposureRate = 4;

    private float _softDropletCap = 0;

    private readonly List<RainDroplet> _droplets = new(HardDropletCap);

    private readonly Vector2[] _pos = new Vector2[HardDropletCap];
    private readonly Vector2[] _data = new Vector2[HardDropletCap];

    private const float Gravity = 0.125f;
    private const float TerminalVelocity = 0.425f;

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var deltaTime = args.DeltaSeconds;

        // WTFFFFF HELP KILL THIS
        if (_playerManager.LocalEntity is { } attachedUid &&
            _entityManager.TransformQuery.TryGetComponent(attachedUid, out var transformComponent) &&
            transformComponent.MapUid is { } mapUid &&
            _wetMapQuery.TryGetComponent(mapUid, out var wetMapComponent) &&
            _transformSystem.GetGrid((attachedUid, transformComponent)) is { } gridUid &&
            _gridQuery.TryGetComponent(gridUid, out var gridComponent) &&
            _transformSystem.TryGetGridTilePosition((attachedUid, transformComponent), out var tilePosition, grid: gridComponent))
        {
            if (_weatherSystem.CanWeatherAffect((gridUid, gridComponent), _mapSystem.GetTileRef((gridUid, gridComponent), tilePosition)))
            {
                DebugTools.Assert(_softDropletCap <= HardDropletCap, $"Soft droplet cap on map {_entityManager.ToPrettyString(mapUid)} of {_softDropletCap} is higher than hard droplet cap of {HardDropletCap}");
                _softDropletCap = MathF.Min(wetMapComponent.SoftDropletCap, _softDropletCap + DropletExposureRate * deltaTime);
            }
            else
                _softDropletCap = 0;
        }
        else
            _softDropletCap = 0;

        var softDropletCapInt = (int)_softDropletCap;
        for (var i = 0; i < _droplets.Count; i++)
        {
            var d = _droplets[i];
            d.Age += deltaTime;

            // ─────────────────────────────
            // GRAVITY CURVE (IMPORTANT PART)
            // ─────────────────────────────

            // ease-in gravity (starts slow, accelerates)
            var gravityFactor = d.Age * d.Age;

            d.Velocity.Y -= Gravity * gravityFactor * deltaTime;

            // terminal velocity clamp
            if (d.Velocity.Y < -TerminalVelocity)
                d.Velocity.Y = -TerminalVelocity;

            // slight horizontal drift (visceral realism)
            d.Position.X += MathF.Sin(d.Seed + d.Age) * 0.002f * deltaTime;

            d.Position += d.Velocity * deltaTime;

            // ─────────────────────────────
            // RESET (smooth respawn)
            // ─────────────────────────────
            if (d.Position.Y < -0.15f)
            {
                if (i < softDropletCapInt)
                    d = CreateDroplet(randomY: true);
                else
                {
                    _droplets.RemoveAt(i);
                    continue;
                }
            }

            _droplets[i] = d;
        }

        var flux = softDropletCapInt - _droplets.Count;
        if (flux <= 0)
            return;

        for (var i = 0; i < flux; i++)
            _droplets.Add(CreateDroplet(randomY: true));
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture is not { })
            return;

        var handle = args.WorldHandle;
        var maxCount = Math.Min(_droplets.Count, HardDropletCap);

        for (var i = 0; i < maxCount; i++)
        {
            var d = _droplets[i];
            _pos[i] = d.Position;

            var speed = MathF.Abs(d.Velocity.Y);

            // normalize into usable shader range
            var streak = Math.Clamp(speed / TerminalVelocity, 0f, 1f);

            _data[i] = new Vector2(d.Size, streak);
        }

        handle.UseShader(_shader);

        _shader.SetParameter("drop_count", maxCount);
        _shader.SetParameter("drops_pos", _pos);
        _shader.SetParameter("drops_data", _data);
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);

        handle.DrawRect(args.WorldBounds, Color.White);

        handle.UseShader(null);
    }

    private RainDroplet CreateDroplet(bool randomY)
    {
        return new RainDroplet
        {
            Position = new Vector2(
                _robustRandom.NextFloat(),
                randomY ? _robustRandom.NextFloat() : 1.2f),

            Velocity = new Vector2(
                0f,
                -_robustRandom.NextFloat(0.02f, TerminalVelocity)
            ),

            Size = _robustRandom.NextFloat(0.035f, 0.065f),

            Streak = 0f,

            Age = _robustRandom.NextFloat(1f),

            Seed = _robustRandom.NextFloat() * 10f
        };
    }

    public struct RainDroplet
    {
        public Vector2 Position;
        public Vector2 Velocity;

        public float Size;
        public float Streak;

        public float Age;
        public float Seed;
    }
}
