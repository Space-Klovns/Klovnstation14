#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.CCVar;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Shooting in the dark (<c>NPCCombatSystem.Klovn.DarkAim.cs</c>): an NPC that can only just see a hostile aims off it,
///         by less the more light there is; not with night vision, not at one close by, and not with light detection off.
///         The test map's ambient light is black, so anything away from the lamp is fully dark.
/// </summary>
public sealed class KsNpcDarkAimTest : GameTest
{
    public override PoolSettings PoolSettings => new() { ServerLightTree = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsDarkAimTestLamp
  components:
  - type: PointLight
    radius: 4
    energy: 5

- type: entity
  parent: KsSquadTestMobLoner
  id: KsDarkAimTestShooter
  components:
  - type: Inventory
    templateId: human
  - type: ContainerContainer

- type: entity
  parent: ClothingEyesBase
  id: KsDarkAimTestGoggles
  components:
  - type: NpcDarkVision
";

    [TestCase("dark", true)]
    [TestCase("lit", false)]
    [TestCase("goggles", false)]
    [TestCase("close", false)]
    [TestCase("light detection off", false)]
    public async Task TestOnlyABarelySeenHostileIsMissed(string how, bool astray)
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, how != "light detection off");
        var scene = await SetUp();
        var combatSystem = scene.EntManager.System<NPCCombatSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            var (shooterUid, targetUid) = how switch
            {
                "lit" => (scene.ShooterUid, scene.LitTargetUid),
                "close" => (scene.CloseShooterUid, scene.DarkTargetUid),
                _ => (scene.ShooterUid, scene.DarkTargetUid),
            };

            if (how == "goggles")
            {
                var gogglesUid = scene.EntManager.SpawnEntity("KsDarkAimTestGoggles", scene.EntManager.GetComponent<TransformComponent>(shooterUid).Coordinates);
                Assert.That(scene.EntManager.System<InventorySystem>().TryEquip(shooterUid, gogglesUid, "eyes", silent: true, force: true));
            }

            var spread = combatSystem.GetDarkAimSpread(shooterUid, targetUid);
            Assert.That(spread != null, Is.EqualTo(astray), $"spread {spread}");

            if (!astray)
                return;

            // At most what the shooter's own perception allows in pitch dark, whatever that is tuned to.
            var maxSpread = scene.EntManager.GetComponent<NpcPerceptionComponent>(shooterUid).DarkAimSpread;
            Assert.That(spread!.Value.Degrees, Is.GreaterThan(0d).And.LessThanOrEqualTo(maxSpread.Degrees + 0.001));

            var transformSystem = scene.EntManager.System<SharedTransformSystem>();
            var from = transformSystem.GetMapCoordinates(shooterUid).Position;
            var target = transformSystem.GetMapCoordinates(targetUid).Position;

            var first = target;
            combatSystem.KsApplyDarkAim(shooterUid, targetUid, from, ref first);
            var second = target;
            combatSystem.KsApplyDarkAim(shooterUid, targetUid, from, ref second);

            var off = System.Math.Abs(Angle.ShortestDistance((target - from).ToAngle(), (first - from).ToAngle()).Degrees);
            // Rolled anywhere within the spread, so only ever exactly on target by a roll of exactly nothing: an aim never
            //      turned at all is what this is here to catch.
            Assert.That(off, Is.GreaterThan(0d), "turned off the target");
            Assert.That(off, Is.LessThanOrEqualTo(spread.Value.Degrees + 0.001), "within the spread");
            Assert.That((first - from).Length(), Is.EqualTo((target - from).Length()).Within(0.01f), "turned, not moved nearer or further");
            Assert.That(Vector2.Distance(first, second), Is.LessThan(0.001f), "held, not shaking from one tick to the next");
        });
    }

    private sealed record Scene(IEntityManager EntManager, EntityUid ShooterUid, EntityUid CloseShooterUid, EntityUid LitTargetUid, EntityUid DarkTargetUid);

    /// <summary>
    ///     A lamp at 0,0 with a hostile beside it, a hostile in the dark at 15,0, a shooter far from both at 8,3 and one right
    ///         beside the dark hostile.
    /// </summary>
    private async Task<Scene> SetUp()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        Scene scene = default!;

        await server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-10, -5), new Vector2i(20, 5)).Owner;

            SpawnAt(entManager, "KsDarkAimTestLamp", gridUid, 0, 0);
            var litTargetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 1, 0);
            var darkTargetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 15, 0);
            var shooterUid = SpawnAt(entManager, "KsDarkAimTestShooter", gridUid, 8, 3);
            var closeShooterUid = SpawnAt(entManager, "KsDarkAimTestShooter", gridUid, 16, 0);
            scene = new Scene(entManager, shooterUid, closeShooterUid, litTargetUid, darkTargetUid);
        });

        await Pair.RunTicksSync(10);
        return scene;
    }
}
