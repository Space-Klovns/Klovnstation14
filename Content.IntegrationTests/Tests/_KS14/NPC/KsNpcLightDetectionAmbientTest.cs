#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.NPC.Systems;
using Content.Shared._KS14.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Light;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     How lit a target is to an NPC goes by the ambient light a player sees there: the map's day and night
///         (<see cref="LightCycleComponent"/>), which only clients apply to the map's colour, and its roofs, which only the
///         client's lighting shuts it out under. On a planet, both had every target in broad day.
/// </summary>
public sealed class KsNpcLightDetectionAmbientTest : GameTest
{
    // Light levels need a server light tree, which the server only builds if told to at startup.
    public override PoolSettings PoolSettings => new() { ServerLightTree = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsLightAmbientTestTarget
";

    private const string Target = "KsLightAmbientTestTarget";

    /// <summary>
    ///     A map lit white by day: a target out in it is as lit as the client's day and night has it at the time. Two times
    ///         half a day apart, so one is day and the other night, give each its own level.
    /// </summary>
    [Test]
    public async Task TestDayAndNightGoByTheLightCycle()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var entManager = Server.EntMan;
        var timing = Server.ResolveDependency<IGameTiming>();
        var map = await Pair.CreateTestMap();
        EntityUid targetUid = default;
        LightCycleComponent cycleComponent = default!;

        await Server.WaitPost(() =>
        {
            var mapLightComponent = entManager.EnsureComponent<MapLightComponent>(map.MapUid);
            mapLightComponent.AmbientLightColor = Color.White;
            cycleComponent = entManager.EnsureComponent<LightCycleComponent>(map.MapUid);
            // The map is already initialised, so set by hand what map init would.
            cycleComponent.OriginalColor = Color.White;
            // Out on the map, off any grid: a grid that is not the map is roofed throughout (ImplicitRoofComponent).
            targetUid = entManager.Spawn(Target, new MapCoordinates(new System.Numerics.Vector2(50f, 50f), map.MapId));
        });

        await Server.WaitAssertion(() =>
        {
            var lightDetectionSystem = entManager.System<NpcLightDetectionSystem>();
            var lightLevelSystem = entManager.System<LightLevelSystem>();
            var gameTicker = entManager.System<SharedGameTicker>();
            var metaDataSystem = entManager.System<MetaDataSystem>();
            var levels = new float[2];

            for (var i = 0; i < 2; i++)
            {
                cycleComponent.Offset = cycleComponent.Duration * (i * 0.5);
                var time = (float) timing.CurTime
                    .Add(cycleComponent.Offset)
                    .Subtract(gameTicker.RoundStartTimeSpan)
                    .Subtract(metaDataSystem.GetPauseTime(map.MapUid))
                    .TotalSeconds;
                var expected = lightLevelSystem.ColorToLevel(
                    SharedLightCycleSystem.GetColor((map.MapUid, cycleComponent), Color.White, time));

                // A fresh tick's worth: the level is cached a tick.
                entManager.RemoveComponent<Content.Server._KS14.NPC.Components.NpcLightLevelCacheComponent>(targetUid);
                levels[i] = lightDetectionSystem.GetLightLevel(targetUid);
                Assert.That(levels[i], Is.EqualTo(expected).Within(0.02f), $"at offset {cycleComponent.Offset}, as lit as the cycle has it");
            }

            Assert.That(System.MathF.Abs(levels[0] - levels[1]), Is.GreaterThan(0.2f), "half a day apart, day and night should differ");
        });
    }

    /// <summary>
    ///     Under a roof, a target in a map lit white is dark: the client draws the roof's colour, black, over the ambient
    ///         there. Off the roof, it is lit.
    /// </summary>
    [Test]
    public async Task TestARoofShutsOutTheAmbient()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid roofedUid = default, openUid = default;

        await Server.WaitPost(() =>
        {
            entManager.EnsureComponent<MapLightComponent>(map.MapUid).AmbientLightColor = Color.White;
            var grid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-3, -3), new Vector2i(3, 3));
            // Roofed tile by tile, not throughout: the two do not go together (ShuttleSystem.OnGridInit).
            entManager.RemoveComponent<ImplicitRoofComponent>(grid.Owner);
            var roofComponent = entManager.EnsureComponent<RoofComponent>(grid.Owner);
            entManager.System<SharedRoofSystem>().SetRoof((grid.Owner, grid.Comp, roofComponent), new Vector2i(0, 0), true);

            roofedUid = SpawnAt(entManager, Target, grid.Owner, 0, 0);
            openUid = SpawnAt(entManager, Target, grid.Owner, 2, 0);
        });

        await Server.WaitAssertion(() =>
        {
            var lightDetectionSystem = entManager.System<NpcLightDetectionSystem>();
            Assert.That(lightDetectionSystem.GetLightLevel(openUid), Is.GreaterThan(0.9f), "out in the open, it is lit");
            Assert.That(lightDetectionSystem.GetLightLevel(roofedUid), Is.LessThan(0.05f), "under the roof, it is dark");
        });
    }

    /// <summary>
    ///     On a grid that is not the map - a shuttle landed on a planet - a target in a map lit white is dark: such a grid is
    ///         roofed throughout (<see cref="ImplicitRoofComponent"/>, black), as the client draws it.
    /// </summary>
    [Test]
    public async Task TestAShuttleIsRoofedThroughout()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var entManager = Server.EntMan;
        var map = await Pair.CreateTestMap();
        EntityUid targetUid = default;

        await Server.WaitPost(() =>
        {
            entManager.EnsureComponent<MapLightComponent>(map.MapUid).AmbientLightColor = Color.White;
            Assert.That(entManager.HasComponent<ImplicitRoofComponent>(map.Grid.Owner), "the test grid is roofed throughout, as any grid not the map");
            targetUid = entManager.SpawnAtPosition(Target, map.GridCoords);
        });

        await Server.WaitAssertion(() =>
            Assert.That(entManager.System<NpcLightDetectionSystem>().GetLightLevel(targetUid), Is.LessThan(0.05f),
                "aboard, it is dark"));
    }
}
