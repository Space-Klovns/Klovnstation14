#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.NPC.Components;
using Content.Server._KS14.NPC.Queries.Considerations;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC.Systems;
using Content.Server.NPC;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.NPC;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     NPC light detection: targets in the dark go unseen unless close or running, watched ones are followed into
///         the dark for a while, and dim ones take longer to react to. The test map's ambient light is black, so
///         anything away from the test lamp is fully dark.
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

# Notices nothing by movement: no speed reveals a target in the dark to it, and no speed keeps one tracked there.
#   For tests that must pass on light and time alone.
- type: entity
  parent: KsSquadTestMobLoner
  id: KsLightTestObserverBlindToSpeed
  components:
  - type: NpcPerception
    revealSpeed: 1000
    trackSpeed: 1000

# As above, with somewhere to wear goggles.
- type: entity
  parent: KsLightTestObserverBlindToSpeed
  id: KsLightTestObserverWithEyes
  components:
  - type: Inventory
    templateId: human
  - type: ContainerContainer

- type: entity
  parent: ClothingEyesBase
  id: KsLightTestGoggles
  components:
  - type: NpcDarkVision

- type: entity
  parent: ClothingEyesBase
  id: KsLightTestWeakGoggles
  components:
  - type: NpcDarkVision
    strength: 0.01
";

    private const string SpeedBlindObserver = "KsLightTestObserverBlindToSpeed";
    private const string ObserverWithEyes = "KsLightTestObserverWithEyes";
    private const string Goggles = "KsLightTestGoggles";
    private const string WeakGoggles = "KsLightTestWeakGoggles";
    private const string EyesSlot = "eyes";

    /// <summary>
    ///     Goggles let an NPC see a target in the dark from afar, as if it were lit, and taking them off puts it back in
    ///         the dark.
    /// </summary>
    [Test]
    public async Task TestGogglesSeeInTheDark()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var (entManager, _, farNpcUid, _, darkTargetUid) = await SetUp(ObserverWithEyes);
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();
        var inventorySystem = entManager.System<InventorySystem>();

        bool Sees()
        {
            perceptionSystem.UpdateNow(farNpcUid);
            return perceptionSystem.TryGetContact(farNpcUid, darkTargetUid, out var contact) && contact.State == NpcContactState.Visible;
        }

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(Sees(), Is.False, "without goggles, the target in the dark should go unseen");

            var gogglesUid = entManager.SpawnEntity(Goggles, entManager.GetComponent<TransformComponent>(farNpcUid).Coordinates);
            Assert.That(inventorySystem.TryEquip(farNpcUid, gogglesUid, EyesSlot, silent: true, force: true));
            Assert.That(Sees(), "with goggles on, it should be seen");

            Assert.That(inventorySystem.TryUnequip(farNpcUid, EyesSlot, silent: true, force: true));
            perceptionSystem.ForgetContact(farNpcUid, darkTargetUid);
            Assert.That(Sees(), Is.False, "with them off again, it should go unseen");
        });
    }

    /// <summary>
    ///     With goggles on, a target in the dark takes no longer to react to than a lit one.
    /// </summary>
    [Test]
    public async Task TestGogglesReactAsIfLit()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var (entManager, _, farNpcUid, _, darkTargetUid) = await SetUp(ObserverWithEyes);
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();

        await Pair.Server.WaitPost(() =>
        {
            var gogglesUid = entManager.SpawnEntity(Goggles, entManager.GetComponent<TransformComponent>(farNpcUid).Coordinates);
            Assert.That(entManager.System<InventorySystem>().TryEquip(farNpcUid, gogglesUid, EyesSlot, silent: true, force: true));
        });

        // The same wait as TestDarkTargetsTakeLongerToReactTo, which is too short for a target in the dark.
        await SeeFor(perceptionSystem, farNpcUid);

        await Pair.Server.WaitAssertion(() =>
        {
            perceptionSystem.TryGetContact(farNpcUid, darkTargetUid, out var contact);
            Assert.That(contact.Reacted, "a target seen through goggles should be reacted to in the normal time");
        });
    }

    /// <summary>
    ///     How far goggles see into the dark is theirs to say: too weak to bring a pitch dark target up to what the NPC
    ///         needs to spot it, it stays unseen.
    /// </summary>
    [Test]
    public async Task TestWeakGogglesDoNotSeeEverything()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var (entManager, _, farNpcUid, _, darkTargetUid) = await SetUp(ObserverWithEyes);
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            var gogglesUid = entManager.SpawnEntity(WeakGoggles, entManager.GetComponent<TransformComponent>(farNpcUid).Coordinates);
            Assert.That(entManager.System<InventorySystem>().TryEquip(farNpcUid, gogglesUid, EyesSlot, silent: true, force: true));

            perceptionSystem.UpdateNow(farNpcUid);

            Assert.That(perceptionSystem.TryGetContact(farNpcUid, darkTargetUid, out var contact) && contact.State == NpcContactState.Visible, Is.False);
        });
    }

    /// <summary>
    ///     Content: every wearable night vision in the game works for an NPC, however it is made. Goes by what makes a thing
    ///         wearable night vision, so goggles added later are covered without touching this.
    /// </summary>
    [Test]
    public async Task TestEveryWearableNightVisionWorksForNpcs()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var (entManager, _, farNpcUid, _, darkTargetUid) = await SetUp(ObserverWithEyes);
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();
        var inventorySystem = entManager.System<InventorySystem>();
        var protoManager = Pair.Server.ResolveDependency<IPrototypeManager>();
        var factory = Pair.Server.ResolveDependency<IComponentFactory>();

        await Pair.Server.WaitAssertion(() =>
        {
            var found = 0;

            foreach (var proto in protoManager.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || !proto.TryGetComponent<Content.Shared._KS14.PhosphorNightVision.WearablePhosphorNightVisionComponent>(out _, factory))
                    continue;

                found++;

                var gogglesUid = entManager.SpawnEntity(proto.ID, entManager.GetComponent<TransformComponent>(farNpcUid).Coordinates);
                Assert.That(inventorySystem.TryEquip(farNpcUid, gogglesUid, EyesSlot, silent: true, force: true), proto.ID);

                perceptionSystem.ForgetContact(farNpcUid, darkTargetUid);
                perceptionSystem.UpdateNow(farNpcUid);

                Assert.That(perceptionSystem.TryGetContact(farNpcUid, darkTargetUid, out var contact) && contact.State == NpcContactState.Visible,
                    $"an NPC wearing {proto.ID} should see in the dark");

                Assert.That(inventorySystem.TryUnequip(farNpcUid, EyesSlot, silent: true, force: true), proto.ID);
                entManager.DeleteEntity(gogglesUid);
            }

            Assert.That(found, Is.GreaterThan(0), "there should be wearable night vision to check");
        });
    }

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
    ///     With the same time in sight, a lit target is reacted to while a dark one is not yet. The dark one is only
    ///         seen at all because it was spotted running, and is followed into stillness from there.
    /// </summary>
    [Test]
    public async Task TestDarkTargetsTakeLongerToReactTo()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var (entManager, _, farNpcUid, litTargetUid, darkTargetUid) = await SetUp();
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();

        await Pair.Server.WaitPost(() => SeeRunning(entManager, perceptionSystem, farNpcUid, darkTargetUid));

        // 0.9s in sight: past the 0.6s reaction time, well short of the ~1.2s it takes in the dark.
        await SeeFor(perceptionSystem, farNpcUid);

        await Pair.Server.WaitAssertion(() =>
        {
            perceptionSystem.TryGetContact(farNpcUid, litTargetUid, out var litContact);
            perceptionSystem.TryGetContact(farNpcUid, darkTargetUid, out var darkContact);

            Assert.Multiple(() =>
            {
                Assert.That(litContact.Reacted, "a lit target should be reacted to after the normal reaction time");
                Assert.That(darkContact.State, Is.EqualTo(NpcContactState.Visible), "the dark target should still be tracked");
                Assert.That(darkContact.Reacted, Is.False, "a target in the dark should take longer to react to");
            });
        });
    }

    /// <summary>
    ///     A target already being watched stays seen, and reacted to, when it steps into the dark - for a while.
    ///         Standing still there long enough loses it.
    /// </summary>
    /// <remarks>
    ///     On light and time alone: the observer notices nothing by movement, and the target never moves - it is
    ///         put in the dark, not walked there.
    /// </remarks>
    [Test]
    public async Task TestWatchedTargetIsFollowedIntoDarkUntilItGoesStill()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var (entManager, _, farNpcUid, litTargetUid, darkTargetUid) = await SetUp(SpeedBlindObserver);
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        await SeeFor(perceptionSystem, farNpcUid);

        await Pair.Server.WaitPost(() =>
            transformSystem.SetCoordinates(litTargetUid, entManager.GetComponent<TransformComponent>(darkTargetUid).Coordinates.Offset(new System.Numerics.Vector2(0f, 1f))));

        // A tick on, so its light level is computed afresh in the dark rather than read from this tick's cache.
        await Pair.RunTicksSync(1);
        await Pair.Server.WaitPost(() => perceptionSystem.UpdateNow(farNpcUid));

        await Pair.Server.WaitAssertion(() =>
        {
            AssertStandingStill(entManager, litTargetUid);
            perceptionSystem.TryGetContact(farNpcUid, litTargetUid, out var contact);
            Assert.That(contact.State, Is.EqualTo(NpcContactState.Visible), "a watched target should be followed into the dark");
            Assert.That(contact.Reacted, "a target already reacted to should stay reacted to in the dark");
        });

        await Pair.RunTicksSync(90); // 3s still, past the 2.5s dark track time

        await Pair.Server.WaitAssertion(() =>
        {
            perceptionSystem.UpdateNow(farNpcUid);
            perceptionSystem.TryGetContact(farNpcUid, litTargetUid, out var contact);
            Assert.That(contact.State, Is.EqualTo(NpcContactState.Lost), "a target standing still in the dark should be lost");
        });
    }

    /// <summary>
    ///     The flicker this fixes: a watched target going in and out of shadow, half a second at a time, stays seen
    ///         the whole way, and stays reacted to.
    /// </summary>
    /// <remarks>
    ///     On light and time alone, as <see cref="TestWatchedTargetIsFollowedIntoDarkUntilItGoesStill"/>: the
    ///         observer notices nothing by movement, and the target is put in each spot rather than walked there.
    ///         Each look comes a tick after the move, so the light level is the new spot's and not a cached one.
    /// </remarks>
    [Test]
    public async Task TestTargetInAndOutOfShadowStaysSeen()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var (entManager, _, farNpcUid, litTargetUid, darkTargetUid) = await SetUp(SpeedBlindObserver);
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var lightDetectionSystem = entManager.System<NpcLightDetectionSystem>();

        EntityCoordinates litSpot = default;
        EntityCoordinates darkSpot = default;

        await Pair.Server.WaitPost(() =>
        {
            litSpot = entManager.GetComponent<TransformComponent>(litTargetUid).Coordinates;
            darkSpot = entManager.GetComponent<TransformComponent>(darkTargetUid).Coordinates.Offset(new System.Numerics.Vector2(0f, 1f));
        });

        await SeeFor(perceptionSystem, farNpcUid);

        for (var i = 0; i < 8; i++)
        {
            var inShadow = i % 2 == 0;

            await Pair.Server.WaitPost(() => transformSystem.SetCoordinates(litTargetUid, inShadow ? darkSpot : litSpot));
            await Pair.RunTicksSync(1);

            await Pair.Server.WaitAssertion(() =>
            {
                AssertStandingStill(entManager, litTargetUid);
                var lightLevel = lightDetectionSystem.GetLightLevel(litTargetUid);
                Assert.That(lightLevel, inShadow ? Is.LessThan(0.03f) : Is.GreaterThanOrEqualTo(0.03f),
                    $"look {i}: the target should be {(inShadow ? "in the dark" : "lit")}");

                perceptionSystem.UpdateNow(farNpcUid);
                perceptionSystem.TryGetContact(farNpcUid, litTargetUid, out var contact);
                Assert.That(contact.State, Is.EqualTo(NpcContactState.Visible),
                    $"look {i}: a watched target {(inShadow ? "in the shadow" : "back in the light")} should still be seen");
                Assert.That(contact.Reacted, $"look {i}: it should still be reacted to");
            });

            await Pair.RunTicksSync(14); // the rest of the half second
        }
    }

    private static void AssertStandingStill(IEntityManager entManager, EntityUid uid)
    {
        Assert.That(entManager.System<SharedPhysicsSystem>().GetMapLinearVelocity(uid).Length(), Is.LessThan(0.01f),
            "the target should be standing still, so nothing here is down to movement");
    }

    /// <summary>
    ///     A watched target that keeps moving in the dark, even at a walk, stays tracked past the dark track time.
    ///         Deliberately the movement rule, unlike <see cref="TestWatchedTargetIsFollowedIntoDarkUntilItGoesStill"/>,
    ///         which is its control.
    /// </summary>
    [Test]
    public async Task TestWatchedTargetMovingInDarkStaysTracked()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var (entManager, _, farNpcUid, litTargetUid, darkTargetUid) = await SetUp();
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var physicsSystem = entManager.System<SharedPhysicsSystem>();

        await SeeFor(perceptionSystem, farNpcUid);

        await Pair.Server.WaitPost(() =>
            transformSystem.SetCoordinates(litTargetUid, entManager.GetComponent<TransformComponent>(darkTargetUid).Coordinates.Offset(new System.Numerics.Vector2(0f, 1f))));

        // A tick on, so its light level is computed afresh in the dark rather than read from this tick's cache.
        await Pair.RunTicksSync(1);
        await Pair.Server.WaitPost(() => perceptionSystem.UpdateNow(farNpcUid));

        for (var i = 0; i < 6; i++)
        {
            await Pair.RunTicksSync(15);
            await Pair.Server.WaitPost(() =>
            {
                // Velocity only for the look, so it does not walk off: the NPC sees what it sees at that moment.
                physicsSystem.SetLinearVelocity(litTargetUid, new System.Numerics.Vector2(0f, 2f));
                perceptionSystem.UpdateNow(farNpcUid);
                physicsSystem.SetLinearVelocity(litTargetUid, System.Numerics.Vector2.Zero);
            });
        }

        await Pair.Server.WaitAssertion(() =>
        {
            perceptionSystem.TryGetContact(farNpcUid, litTargetUid, out var contact);
            Assert.That(contact.State, Is.EqualTo(NpcContactState.Visible), "a watched target moving in the dark should stay tracked");
        });
    }

    /// <summary>
    ///     A target in the dark that nobody was watching is not spotted from afar at a walk, but is at a run.
    /// </summary>
    [Test]
    public async Task TestOnlyRunningGivesAwayAnUnwatchedDarkTarget()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var (entManager, _, farNpcUid, _, darkTargetUid) = await SetUp();
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();
        var physicsSystem = entManager.System<SharedPhysicsSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            perceptionSystem.UpdateNow(farNpcUid);
            Assert.That(perceptionSystem.TryGetContact(farNpcUid, darkTargetUid, out _), Is.False, "a still target in the dark goes unseen");

            physicsSystem.SetLinearVelocity(darkTargetUid, new System.Numerics.Vector2(0f, 2.5f));
            perceptionSystem.UpdateNow(farNpcUid);
            Assert.That(perceptionSystem.TryGetContact(farNpcUid, darkTargetUid, out _), Is.False, "walking in the dark goes unseen");

            SeeRunning(entManager, perceptionSystem, farNpcUid, darkTargetUid);
            Assert.That(perceptionSystem.TryGetContact(farNpcUid, darkTargetUid, out var contact) && contact.State == NpcContactState.Visible,
                "running in the dark gives a target away");
        });
    }

    /// <summary>
    ///     Right next to the NPC, a target in the dark is seen anyway, so it takes no longer to react to than a lit one.
    /// </summary>
    [Test]
    public async Task TestDarkTargetUpCloseIsReactedToNormally()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcLightDetection, true);
        var (entManager, npcUid, _, _, darkTargetUid) = await SetUp();
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();

        await SeeFor(perceptionSystem, npcUid);

        await Pair.Server.WaitAssertion(() =>
        {
            perceptionSystem.TryGetContact(npcUid, darkTargetUid, out var contact);
            Assert.That(contact.Reacted, "a dark target right next to the NPC should be reacted to in the normal time");
        });
    }

    /// <summary>
    ///     Has <paramref name="npcUid"/> look around for 0.9s (3 rounds of 9 ticks at 30 TPS): past the 0.6s reaction
    ///         time, well short of the ~1.2s it takes in the dark.
    /// </summary>
    private async Task SeeFor(NpcPerceptionSystem perceptionSystem, EntityUid npcUid)
    {
        for (var i = 0; i < 3; i++)
        {
            await Pair.Server.WaitPost(() => perceptionSystem.UpdateNow(npcUid));
            await Pair.RunTicksSync(9);
        }

        await Pair.Server.WaitPost(() => perceptionSystem.UpdateNow(npcUid));
    }

    /// <summary>
    ///     One look from <paramref name="npcUid"/> while <paramref name="targetUid"/> is running - for the look
    ///         only, so it does not run off.
    /// </summary>
    private static void SeeRunning(IEntityManager entManager, NpcPerceptionSystem perceptionSystem, EntityUid npcUid, EntityUid targetUid)
    {
        var physicsSystem = entManager.System<SharedPhysicsSystem>();
        physicsSystem.SetLinearVelocity(targetUid, new System.Numerics.Vector2(0f, 5f));
        perceptionSystem.UpdateNow(npcUid);
        physicsSystem.SetLinearVelocity(targetUid, System.Numerics.Vector2.Zero);
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
    private async Task<(IEntityManager EntManager, EntityUid NpcUid, EntityUid FarNpcUid, EntityUid LitTargetUid, EntityUid DarkTargetUid)> SetUp(
        string farObserver = LonerMob)
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

            npcUid = SpawnAt(entManager, LonerMob, gridUid, 16, 0);
            farNpcUid = SpawnAt(entManager, farObserver, gridUid, 8, 3);
        });

        await Pair.RunTicksSync(10);

        // Asleep, so only UpdateNow moves their perception along, and the tests decide exactly what each look sees.
        await server.WaitPost(() =>
        {
            var npcSystem = entManager.System<NPCSystem>();
            npcSystem.SleepNPC(npcUid);
            npcSystem.SleepNPC(farNpcUid);
        });

        return (entManager, npcUid, farNpcUid, litTargetUid, darkTargetUid);
    }
}
