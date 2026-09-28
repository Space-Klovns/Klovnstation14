#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.NPC.Components;
using Content.Server._KS14.NPC.Queries.Considerations;
using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC;
using Content.Shared._KS14.CCVar;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     NPC light detection: targets in the dark go unseen unless close, and dim ones take longer to react to.
///         The test map's ambient light is black, so anything away from the test lamp is fully dark.
/// </summary>
public sealed class KsNpcLightDetectionTest : GameTest
{
    // Light levels need a server light tree, which the server only builds if told to at startup.
    public override PoolSettings PoolSettings => new() { ServerLightTree = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsLightTestLamp
  components:
  - type: PointLight
    radius: 4
    energy: 5 # bright enough that the target beside it is fully lit, not merely above the threshold
";

    /// <summary>
    ///     A lit target can be seen from afar, a dark one cannot - unless the NPC is right next to it.
    /// </summary>
    [Test]
    public async Task TestDarkTargetsAreOnlySeenUpClose()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var (entManager, npcUid, farNpcUid, litTargetUid, darkTargetUid) = await SetUp();
        var visibleInLight = new TargetVisibleInLightCon { Curve = new Content.Server.NPC.Queries.Curves.BoolCurve() };

        await Pair.Server.WaitAssertion(() =>
        {
            visibleInLight.Initialise(entManager.EntitySysManager.DependencyCollection);
            var blackboard = new NPCBlackboard();

            Assert.Multiple(() =>
            {
                Assert.That(visibleInLight.GetScore(blackboard, farNpcUid, litTargetUid), Is.EqualTo(1f),
                    "a lit target should be seen from afar");
                Assert.That(visibleInLight.GetScore(blackboard, farNpcUid, darkTargetUid), Is.EqualTo(0f),
                    "a target in the dark should go unseen from afar");
                Assert.That(visibleInLight.GetScore(blackboard, npcUid, darkTargetUid), Is.EqualTo(1f),
                    "a target in the dark should still be seen from right next to it");
            });
        });
    }

    /// <summary>
    ///     With the same time in sight, a lit target is reacted to while a dark one is not yet.
    /// </summary>
    [Test]
    public async Task TestDarkTargetsTakeLongerToReactTo()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var (entManager, _, farNpcUid, litTargetUid, darkTargetUid) = await SetUp();
        var reactionTimeSystem = entManager.System<NpcReactionTimeSystem>();

        // 0.9s in sight (3 rounds of 9 ticks at 30 TPS): past the 0.6s reaction time, well short of the ~1.2s
        //      it takes in the dark.
        for (var i = 0; i < 3; i++)
        {
            await Pair.Server.WaitPost(() =>
            {
                reactionTimeSystem.TrySeeAndReact(farNpcUid, litTargetUid, alert: false);
                reactionTimeSystem.TrySeeAndReact(farNpcUid, darkTargetUid, alert: false);
            });
            await Pair.RunTicksSync(9);
        }

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(reactionTimeSystem.TrySeeAndReact(farNpcUid, litTargetUid, alert: false),
                    "a lit target should be reacted to after the normal reaction time");
                Assert.That(reactionTimeSystem.TrySeeAndReact(farNpcUid, darkTargetUid, alert: false), Is.False,
                    "a target in the dark should take longer to react to");
            });
        });
    }

    /// <summary>
    ///     A target already reacted to stays reacted to when it steps into the dark - it does not become a surprise
    ///         again mid-fight, which would stop the NPC shooting at it.
    /// </summary>
    [Test]
    public async Task TestReactionSticksWhenTargetStepsIntoDark()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var (entManager, _, farNpcUid, litTargetUid, darkTargetUid) = await SetUp();
        var reactionTimeSystem = entManager.System<NpcReactionTimeSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        await SeeFor(reactionTimeSystem, farNpcUid, litTargetUid);

        await Pair.Server.WaitPost(() =>
        {
            Assert.That(reactionTimeSystem.TrySeeAndReact(farNpcUid, litTargetUid, alert: false),
                "the lit target should have been reacted to");
            transformSystem.SetCoordinates(litTargetUid, entManager.GetComponent<TransformComponent>(darkTargetUid).Coordinates);
        });

        await Pair.RunTicksSync(1);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(reactionTimeSystem.TrySeeAndReact(farNpcUid, litTargetUid, alert: false),
                "a target already reacted to should stay reacted to in the dark"));
    }

    /// <summary>
    ///     Right next to the NPC, a target in the dark is seen anyway, so it takes no longer to react to than a lit one.
    /// </summary>
    [Test]
    public async Task TestDarkTargetUpCloseIsReactedToNormally()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var (entManager, npcUid, _, _, darkTargetUid) = await SetUp();
        var reactionTimeSystem = entManager.System<NpcReactionTimeSystem>();

        await SeeFor(reactionTimeSystem, npcUid, darkTargetUid);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(reactionTimeSystem.TrySeeAndReact(npcUid, darkTargetUid, alert: false),
                "a dark target right next to the NPC should be reacted to in the normal time"));
    }

    /// <summary>
    ///     Keeps <paramref name="targetUid"/> in <paramref name="npcUid"/>'s sight for 0.9s (3 rounds of 9 ticks at
    ///         30 TPS): past the 0.6s reaction time, well short of the ~1.2s it takes in the dark.
    /// </summary>
    private async Task SeeFor(NpcReactionTimeSystem reactionTimeSystem, EntityUid npcUid, EntityUid targetUid)
    {
        for (var i = 0; i < 3; i++)
        {
            await Pair.Server.WaitPost(() => reactionTimeSystem.TrySeeAndReact(npcUid, targetUid, alert: false));
            await Pair.RunTicksSync(9);
        }
    }

    /// <summary>
    ///     A target's light level is computed once per tick and cached on it: moving it into the light changes
    ///         nothing until the next tick, when it is computed afresh.
    /// </summary>
    [Test]
    public async Task TestLightLevelIsCachedPerTick()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var (entManager, _, _, litTargetUid, darkTargetUid) = await SetUp();
        var lightDetectionSystem = entManager.System<NpcLightDetectionSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(lightDetectionSystem.GetLightLevel(darkTargetUid), Is.LessThan(0.15f), "the target starts in the dark");
            Assert.That(entManager.HasComponent<NpcLightLevelCacheComponent>(darkTargetUid), "the level should be cached on the target");

            // Same tick: into the light beside the lamp, but still the cached answer.
            transformSystem.SetCoordinates(darkTargetUid, entManager.GetComponent<TransformComponent>(litTargetUid).Coordinates);
            Assert.That(lightDetectionSystem.GetLightLevel(darkTargetUid), Is.LessThan(0.15f),
                "within one tick, the cached level should be reused");
        });

        await Pair.RunTicksSync(1);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(lightDetectionSystem.GetLightLevel(darkTargetUid), Is.GreaterThanOrEqualTo(0.15f),
                "on a new tick, the level should be computed afresh"));
    }

    /// <summary>
    ///     A lit flare lights up whoever stands in its glow. Its light is animated only on the client, so without
    ///         the server-side mirror the server's copy stays disabled, at the prototype's radius of 1, and lights
    ///         nothing.
    /// </summary>
    [Test]
    public async Task TestLitFlareLightsItsSurroundings()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var (entManager, _, _, _, darkTargetUid) = await SetUp();
        var lightDetectionSystem = entManager.System<NpcLightDetectionSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        EntityUid flareUid = default;
        await Pair.Server.WaitPost(() =>
        {
            var targetCoordinates = entManager.GetComponent<TransformComponent>(darkTargetUid).Coordinates;
            flareUid = entManager.SpawnEntity("Flare", targetCoordinates.Offset(new System.Numerics.Vector2(-2f, 0f)));
        });

        await Pair.RunTicksSync(2);
        await Pair.Server.WaitAssertion(() =>
            Assert.That(lightDetectionSystem.GetLightLevel(darkTargetUid), Is.LessThan(0.15f), "an unlit flare lights nothing"));

        await Pair.Server.WaitPost(() =>
            entManager.System<Content.Server.Light.EntitySystems.ExpendableLightSystem>()
                .TryActivate((flareUid, entManager.GetComponent<Content.Server.Light.Components.ExpendableLightComponent>(flareUid))));

        await Pair.RunTicksSync(2);
        await Pair.Server.WaitAssertion(() =>
            Assert.That(lightDetectionSystem.GetLightLevel(darkTargetUid), Is.GreaterThanOrEqualTo(0.15f),
                "someone standing two tiles from a lit flare should be lit"));
    }

    /// <summary>
    ///     With the cvar off, light makes no difference.
    /// </summary>
    [Test]
    public async Task TestOffByDefault()
    {
        var (entManager, _, farNpcUid, _, darkTargetUid) = await SetUp();
        var lightDetectionSystem = entManager.System<NpcLightDetectionSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(lightDetectionSystem.GetLightLevel(darkTargetUid), Is.EqualTo(1f),
                "with light detection off, every target counts as lit");
        });
    }

    /// <summary>
    ///     A near NPC and a far NPC, a lamp with a target beside it, and a target in the dark.
    /// </summary>
    private async Task<(IEntityManager EntManager, EntityUid NpcUid, EntityUid FarNpcUid, EntityUid LitTargetUid, EntityUid DarkTargetUid)> SetUp()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();

        EntityUid npcUid = default;
        EntityUid farNpcUid = default;
        EntityUid litTargetUid = default;
        EntityUid darkTargetUid = default;

        await server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-10, -5), new Vector2i(20, 5)).Owner;

            SpawnAt(entManager, "KsLightTestLamp", gridUid, 0, 0);
            litTargetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 1, 0);
            darkTargetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 15, 0);

            npcUid = SpawnAt(entManager, SyndicateMob, gridUid, 16, 0);
            farNpcUid = SpawnAt(entManager, SyndicateMob, gridUid, 8, 3);
        });

        await Pair.RunTicksSync(10);

        return (entManager, npcUid, farNpcUid, litTargetUid, darkTargetUid);
    }
}
