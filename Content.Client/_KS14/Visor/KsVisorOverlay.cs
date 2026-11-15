using System.Numerics;
using Content.Client._KS14.Portal;
using Content.Client.Weather;
using Content.Shared._KS14.Weather;
using Content.Shared.StatusEffectNew;
using Content.Shared.Weather;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Visor;

/// <summary>
///     Draws the water on the local player's visor (<see cref="KsVisorSimulation"/>) over the screen, refracting what
///         lies behind it. Fed by the precipitation (<see cref="KsVisorPrecipitationComponent"/>) of the weather on the
///         player's map, while they're out in it.
/// </summary>
public sealed partial class KsVisorOverlay : Overlay
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private EntityManager _entityManager = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private WeatherSystem _weatherSystem = default!;
    [Dependency] private StatusEffectsSystem _statusEffectsSystem = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private EntityQuery<KsVisorPrecipitationComponent> _precipitationQuery = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader;
    private readonly KsVisorSimulation _simulation;

    // Vector2 arrays, not Color ones: Clyde converts every Color uniform from sRGB to linear, which would bend the
    //      positions and sizes packed in them.
    private readonly Vector2[] _dropPositions = new Vector2[KsVisorSimulation.MaxDrops];
    private readonly Vector2[] _dropSizes = new Vector2[KsVisorSimulation.MaxDrops];
    private readonly Vector2[] _dropTrails = new Vector2[KsVisorSimulation.MaxDrops];

    public KsVisorOverlay(ShaderInstance shader, IRobustRandom random)
    {
        _shader = shader;
        _simulation = new KsVisorSimulation(random);
    }

    protected override void DisposeBehavior()
    {
        _shader.Dispose();
        base.DisposeBehavior();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var screenSize = _clyde.ScreenSize;
        var aspect = screenSize.Y > 0 ? (float)screenSize.X / (float)screenSize.Y : 1f;
        _simulation.Update(args.DeltaSeconds, GetExposure(), aspect);
    }

    /// <summary>
    ///     What the weather on the local player's map is putting on their visor now.
    /// </summary>
    private KsVisorExposure GetExposure()
    {
        if (_playerManager.LocalEntity is not { } playerUid ||
            !_entityManager.TransformQuery.TryComp(playerUid, out var transformComponent) ||
            transformComponent.MapUid is not { } mapUid ||
            !_statusEffectsSystem.TryEffectsWithComp<WeatherStatusEffectComponent>(mapUid, out var weathers))
            return KsVisorExposure.None;

        var dropsPerSecond = 0f;
        var flakesPerSecond = 0f;
        var mist = 0f;
        var dropScale = 1f;

        foreach (var weather in weathers)
        {
            if (!_precipitationQuery.TryComp(weather.Owner, out var precipitationComponent))
                continue;

            var strength = _weatherSystem.GetWeatherPercent((weather.Owner, weather.Comp2));
            dropsPerSecond += precipitationComponent.DropsPerSecond * strength;
            flakesPerSecond += precipitationComponent.FlakesPerSecond * strength;
            mist = MathF.Max(mist, precipitationComponent.Mist * strength);
            dropScale = MathF.Max(dropScale, precipitationComponent.DropScale);
        }

        return new KsVisorExposure(dropsPerSecond, flakesPerSecond, mist, dropScale, IsSheltered((playerUid, transformComponent)));
    }

    /// <summary>
    ///     Whether something overhead keeps the weather off: a roof, or anything else that blocks it on this tile.
    /// </summary>
    private bool IsSheltered(Entity<TransformComponent> player)
    {
        if (player.Comp.GridUid is not { } gridUid ||
            !_gridQuery.TryComp(gridUid, out var gridComponent))
            return false;

        var tileRef = _mapSystem.GetTileRef((gridUid, gridComponent), player.Comp.Coordinates);
        return !_weatherSystem.CanWeatherAffect((gridUid, gridComponent), tileRef);
    }

    // Nothing to draw is nothing to copy the screen for. Not drawn into portal views either: the water is on the
    //      viewer's visor, not on whatever a portal shows.
    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return !_simulation.IsDry && !KsPortalRenderSystem.PassActive;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture is not { } screenTexture)
            return;

        var count = Math.Min(_simulation.Drops.Count, KsVisorSimulation.MaxDrops);
        for (var i = 0; i < count; i++)
        {
            var drop = _simulation.Drops[i];
            _dropPositions[i] = drop.Position;
            // TODO ENGINE: three arrays, two values from 0 to 1 sharing each y, only because the engine keeps built-in
            //      uniform locations in an sbyte; see the top of visor_rain.swsl.
            _dropSizes[i] = new Vector2(KsVisorSimulation.GetDrawnRadius(drop),
                KsVisorSimulation.Pack(KsVisorSimulation.GetAlpha(drop), KsVisorSimulation.GetGathered(drop)));
            _dropTrails[i] = new Vector2(drop.Trail, KsVisorSimulation.Pack(drop.Melt, drop.Seed));
        }

        _shader.SetParameter("SCREEN_TEXTURE", screenTexture);
        _shader.SetParameter("drop_count", count);
        _shader.SetParameter("drop_positions", _dropPositions);
        _shader.SetParameter("drop_sizes", _dropSizes);
        _shader.SetParameter("drop_trails", _dropTrails);
        _shader.SetParameter("mist", _simulation.Mist);

        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
