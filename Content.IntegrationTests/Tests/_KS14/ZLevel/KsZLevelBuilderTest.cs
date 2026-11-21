#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Weather;
using Content.Server._KS14.ZLevel.Builder;
using Content.Server.Atmos.EntitySystems;
using Content.Server.GameTicking;
using Content.Server.Weather;
using Content.Shared._KS14.Ladders;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Builder;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Gravity;
using Content.Shared.Interaction.Events;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.ZLevel;

/// <summary>
///     The level builder puts linked ladders between the user's tile and the spot above it, creating the level above,
///         static grid and all, when there is none, at most once per cooldown, and that level carries and keeps
///         following its source map's environment.
/// </summary>
[TestFixture]
[TestOf(typeof(KsZLevelBuilderSystem))]
public sealed class KsZLevelBuilderTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _builder = "KsTestZLevelBuilder";
    private readonly EntProtoId _quickBuilder = "KsTestZLevelBuilderQuick";
    private readonly EntProtoId _rule = "KsTestZLevelBuilderWeatherRule";
    private readonly EntProtoId _rain = "KsTestZLevelBuilderRain";
    private readonly EntProtoId _snow = "KsTestZLevelBuilderSnow";
    private static readonly ProtoId<ContentTileDefinition> Plating = "Plating";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestZLevelBuilderLadderLower
  components:
  - type: KsLadder

- type: entity
  id: KsTestZLevelBuilderLadderUpper
  components:
  - type: KsLadder

- type: entity
  id: KsTestZLevelBuilder
  components:
  - type: UseDelay
    delays:
      ksZLevelBuilderLevel:
        length: 600
  - type: KsZLevelBuilder
    lowerLadder: KsTestZLevelBuilderLadderLower
    upperLadder: KsTestZLevelBuilderLadderUpper

- type: entity
  id: KsTestZLevelBuilderQuick
  components:
  - type: UseDelay
    delays:
      ksZLevelBuilderLevel:
        length: 0.1
  - type: KsZLevelBuilder
    lowerLadder: KsTestZLevelBuilderLadderLower
    upperLadder: KsTestZLevelBuilderLadderUpper

- type: entity
  parent: WeatherBase
  id: KsTestZLevelBuilderRain
  components:
  - type: WeatherStatusEffect
    sprite:
      sprite: /Textures/Effects/weather.rsi
      state: rain

- type: entity
  parent: WeatherBase
  id: KsTestZLevelBuilderSnow
  components:
  - type: WeatherStatusEffect
    sprite:
      sprite: /Textures/Effects/weather.rsi
      state: snowfall_light

- type: entity
  parent: BaseGameRule
  id: KsTestZLevelBuilderWeatherRule
  components:
  - type: KsDynamicWeatherRule
    initialState: Snowy
    states:
    - name: Snowy
      weathers:
      - KsTestZLevelBuilderSnow
      minDuration: 600
      maxDuration: 600
    - name: Rainy
      weathers:
      - KsTestZLevelBuilderRain
      minDuration: 600
      maxDuration: 600
";

    [Test]
    public async Task UseCreatesTheLevelAboveWithAStaticGridAndLinkedLadders()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var zLevelSystem = server.System<KsZLevelSystem>();
        EntityUid userUid = default;
        EntityUid builderUid = default;

        await server.WaitPost(() =>
        {
            userUid = SpawnUserOn(testMap.Grid, Vector2i.Zero);
            builderUid = server.EntMan.SpawnEntity(_builder, MapCoordinates.Nullspace);
            Use(builderUid, userUid);
        });
        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(zLevelSystem.TryGetZLevelAbove(testMap.MapUid, out var aboveEntity), Is.True, "the level above was created");
            var upperMapUid = aboveEntity!.Value.Owner;

            var upperGrids = GridsOn(upperMapUid);
            Assert.That(upperGrids, Has.Count.EqualTo(1), "one grid for the upper ladder to stand on");
            Assert.That(server.EntMan.GetComponent<PhysicsComponent>(upperGrids[0]).BodyType, Is.EqualTo(BodyType.Static),
                "the grid it made is static, so nothing pushes it or sets it drifting");

            var lowerLadder = LaddersOn(testMap.MapUid).Single();
            var upperLadder = LaddersOn(upperMapUid).Single();
            Assert.That(lowerLadder.Comp.Linked, Is.EqualTo(upperLadder.Owner), "the lower ladder leads up");
            Assert.That(upperLadder.Comp.Linked, Is.EqualTo(lowerLadder.Owner), "the upper ladder leads down");

            var lowerPosition = server.System<SharedTransformSystem>().GetWorldPosition(lowerLadder.Owner);
            var upperPosition = server.System<SharedTransformSystem>().GetWorldPosition(upperLadder.Owner);
            Assert.That(Vector2.Distance(lowerPosition, upperPosition), Is.LessThan(0.01f), "the upper ladder is directly above");
        });
    }

    [Test]
    public async Task CooldownStopsANewLevelButNotLaddersOntoAnExistingOne()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var zLevelSystem = server.System<KsZLevelSystem>();
        var builderSystem = server.System<KsZLevelBuilderSystem>();
        EntityUid userUid = default;
        EntityUid builderUid = default;
        EntityUid upperMapUid = default;

        await server.WaitPost(() =>
        {
            userUid = SpawnUserOn(testMap.Grid, Vector2i.Zero);
            builderUid = server.EntMan.SpawnEntity(_builder, MapCoordinates.Nullspace);
            Assert.That(builderSystem.TryBuild(Builder(builderUid), userUid), Is.True);
            upperMapUid = AboveOf(testMap.MapUid);

            // Onto the existing level, from another tile: the cooldown has nothing to say about that.
            MoveUserTo(userUid, testMap.Grid, new Vector2i(1, 0));
            Assert.That(builderSystem.TryBuild(Builder(builderUid), userUid), Is.True, "a ladder onto an existing level is built during the cooldown");
            Assert.That(zLevelSystem.TryGetZLevelAbove(upperMapUid, out _), Is.False, "that made no new level");
            Assert.That(LaddersOn(upperMapUid), Has.Count.EqualTo(2));

            // Up there, with nothing above: that needs a new level, which the cooldown refuses.
            var upperGrid = GridsOn(upperMapUid)[0];
            MoveUserTo(userUid, (upperGrid, server.EntMan.GetComponent<MapGridComponent>(upperGrid)), new Vector2i(0, 1));
            Assert.That(builderSystem.TryBuild(Builder(builderUid), userUid), Is.False, "no new level during the cooldown");
            Assert.That(zLevelSystem.TryGetZLevelAbove(upperMapUid, out _), Is.False, "the cooldown stopped another level");
        });
    }

    [Test]
    public async Task NewLevelCarriesTheSourceEnvironmentAndFollowsItsWeather()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var weatherSystem = server.System<WeatherSystem>();
        var ruleSystem = server.System<KsDynamicWeatherRuleSystem>();
        var ambientColour = Color.FromHex("#D8B059");
        var lightCycleOffset = TimeSpan.FromMinutes(7);
        EntityUid ruleUid = default;
        EntityUid upperMapUid = default;

        await server.WaitPost(() =>
        {
            var mapUid = testMap.MapUid;
            server.System<SharedMapSystem>().SetAmbientLight(testMap.MapId, ambientColour);
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = 250f };
            mixture.AdjustMoles(Gas.Oxygen, 12f);
            server.System<AtmosphereSystem>().SetMapAtmosphere(mapUid, false, mixture);
            // Inherent, as a planet's is: a map's gravity is otherwise worked out from its generators, and it has none.
            var gravity = server.EntMan.EnsureComponent<GravityComponent>(mapUid);
            gravity.Enabled = true;
            gravity.Inherent = true;
            var lightCycle = server.EntMan.EnsureComponent<LightCycleComponent>(mapUid);
            server.System<SharedLightCycleSystem>().SetOffset((mapUid, lightCycle), lightCycleOffset);

            Assert.That(server.System<GameTicker>().StartGameRule(_rule, out ruleUid), Is.True);
            ruleSystem.SetMap((ruleUid, server.EntMan.GetComponent<KsDynamicWeatherRuleComponent>(ruleUid)), mapUid);
        });
        await Pair.RunTicksSync(3);

        await server.WaitPost(() =>
        {
            Assert.That(weatherSystem.HasWeather(testMap.MapId, _snow), Is.True, "the source map has its weather before the level is built");
            var userUid = SpawnUserOn(testMap.Grid, Vector2i.Zero);
            var builderUid = server.EntMan.SpawnEntity(_builder, MapCoordinates.Nullspace);
            Assert.That(server.System<KsZLevelBuilderSystem>().TryBuild(Builder(builderUid), userUid), Is.True);
            upperMapUid = AboveOf(testMap.MapUid);
        });
        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            var upperMapId = server.EntMan.GetComponent<MapComponent>(upperMapUid).MapId;
            Assert.That(server.EntMan.GetComponent<MapLightComponent>(upperMapUid).AmbientLightColor, Is.EqualTo(ambientColour), "lighting");
            var upperAtmosphere = server.EntMan.GetComponent<MapAtmosphereComponent>(upperMapUid);
            Assert.That(upperAtmosphere.Space, Is.False, "atmosphere");
            var upperMixture = upperAtmosphere.Mixture;
            Assert.That(upperMixture.GetMoles(Gas.Oxygen), Is.EqualTo(12f).Within(0.001f));
            Assert.That(upperMixture.Temperature, Is.EqualTo(250f).Within(0.001f));
            Assert.That(server.EntMan.GetComponent<GravityComponent>(upperMapUid).Enabled, Is.True, "gravity");
            Assert.That(server.EntMan.GetComponent<LightCycleComponent>(upperMapUid).Offset, Is.EqualTo(lightCycleOffset), "the same time of day");
            Assert.That(weatherSystem.HasWeather(upperMapId, _snow), Is.True, "the weather at the time");

            Assert.That(ruleSystem.TrySetState((ruleUid, server.EntMan.GetComponent<KsDynamicWeatherRuleComponent>(ruleUid)), "Rainy"), Is.True);
            Assert.That(weatherSystem.HasWeather(upperMapId, _rain), Is.True, "a weather change made afterwards reaches the level");
        });

        await RunSeconds((float)Content.Shared.Weather.SharedWeatherSystem.ShutdownTime.TotalSeconds + 2f);
        await server.WaitAssertion(() =>
        {
            var upperMapId = server.EntMan.GetComponent<MapComponent>(upperMapUid).MapId;
            Assert.That(weatherSystem.HasWeather(upperMapId, _snow), Is.False, "and the weather it replaced fades out there too");
        });

        await server.WaitPost(() => server.System<GameTicker>().EndGameRule(ruleUid));
    }

    [Test]
    public async Task ThreeUsesStackThreeLevels()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var zLevelSystem = server.System<KsZLevelSystem>();
        var builderSystem = server.System<KsZLevelBuilderSystem>();
        EntityUid userUid = default;
        EntityUid builderUid = default;
        var levelUids = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            userUid = SpawnUserOn(testMap.Grid, Vector2i.Zero);
            builderUid = server.EntMan.SpawnEntity(_quickBuilder, MapCoordinates.Nullspace);
            levelUids.Add(testMap.MapUid);
        });

        for (var i = 0; i < 3; i++)
        {
            await server.WaitPost(() =>
            {
                Assert.That(builderSystem.TryBuild(Builder(builderUid), userUid), Is.True, $"use {i + 1}");
                var aboveUid = AboveOf(levelUids[^1]);
                levelUids.Add(aboveUid);

                // Steps off the new ladder onto a tile beside it, ready to go up again.
                var upperGrid = GridsOn(aboveUid)[0];
                MoveUserTo(userUid, (upperGrid, server.EntMan.GetComponent<MapGridComponent>(upperGrid)), new Vector2i(0, 1));
            });
            await RunSeconds(0.5f);
        }

        await server.WaitAssertion(() =>
        {
            var stack = new List<Entity<KsZLevelComponent>>();
            Assert.That(zLevelSystem.TryGetStack(testMap.MapUid, stack), Is.True);
            Assert.That(stack.Select(entity => entity.Owner), Is.EqualTo(levelUids), "three levels stacked in order on the first");
        });
    }

    private Entity<KsZLevelBuilderComponent> Builder(EntityUid builderUid)
    {
        return (builderUid, Pair.Server.EntMan.GetComponent<KsZLevelBuilderComponent>(builderUid));
    }

    private void Use(EntityUid builderUid, EntityUid userUid)
    {
        var ev = new UseInHandEvent(userUid);
        Pair.Server.EntMan.EventBus.RaiseLocalEvent(builderUid, ev);
        Assert.That(ev.Handled, Is.True);
    }

    private EntityUid SpawnUserOn(Entity<MapGridComponent> grid, Vector2i indices)
    {
        var userUid = Pair.Server.EntMan.SpawnEntity(null, new EntityCoordinates(grid.Owner, 0.5f, 0.5f));
        MoveUserTo(userUid, grid, indices);
        return userUid;
    }

    private void MoveUserTo(EntityUid userUid, Entity<MapGridComponent> grid, Vector2i indices)
    {
        var mapSystem = Pair.Server.System<SharedMapSystem>();
        var plating = Pair.Server.ResolveDependency<ITileDefinitionManager>()[Plating];
        mapSystem.SetTile(grid.Owner, grid.Comp, indices, new Tile(plating.TileId));
        Pair.Server.System<SharedTransformSystem>().SetCoordinates(userUid, mapSystem.GridTileToLocal(grid.Owner, grid.Comp, indices));
    }

    private EntityUid AboveOf(EntityUid mapUid)
    {
        Assert.That(Pair.Server.System<KsZLevelSystem>().TryGetZLevelAbove(mapUid, out var aboveEntity), Is.True);
        return aboveEntity!.Value.Owner;
    }

    private List<EntityUid> GridsOn(EntityUid mapUid)
    {
        var grids = new List<EntityUid>();
        var query = Pair.Server.EntMan.AllEntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var gridUid, out _, out var transformComponent))
        {
            if (transformComponent.MapUid == mapUid && gridUid != mapUid)
                grids.Add(gridUid);
        }

        return grids;
    }

    private List<Entity<KsLadderComponent>> LaddersOn(EntityUid mapUid)
    {
        var ladders = new List<Entity<KsLadderComponent>>();
        var query = Pair.Server.EntMan.AllEntityQueryEnumerator<KsLadderComponent, TransformComponent>();
        while (query.MoveNext(out var ladderUid, out var ladderComponent, out var transformComponent))
        {
            if (transformComponent.MapUid == mapUid)
                ladders.Add((ladderUid, ladderComponent));
        }

        return ladders;
    }
}
