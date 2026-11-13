#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Hiding;
using Content.Server._KS14.NPC.Recovery;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Tools.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     <see cref="NpcHidingSystem"/>: an NPC hiding in a closet to heal gets in, stays while its meds work, and gets out once
///         they have, a hostile comes right up to it, or it is hurt in there. Choosing to is the operative HTN's, in
///         <see cref="KsOperativeHtnRootTest"/>.
/// </summary>
public sealed class KsNpcHidingTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly ProtoId<DamageTypePrototype> BluntDamage = "Blunt";

    private const string HiderMob = "KsHidingTestMob";

    /// <summary>
    ///     Critical at 100, by its parent, and over its meds at 30 damage or less.
    /// </summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsHidingTestMob
  components:
  - type: Hands
  - type: DoAfter
  - type: NpcRecovery
    maxDuration: 1000
  - type: NpcClosetHealer
";

    /// <summary>
    ///     In it, shut, until its meds have worked: then out, and the closet open.
    /// </summary>
    [Test]
    public async Task TestStaysInUntilHealed()
    {
        var scene = await SetUp();

        await Pair.Server.WaitAssertion(() =>
        {
            SetDamage(scene.EntManager, scene.MobUid, 50);
            Assert.That(scene.HidingSystem.TryHideIn(scene.MobUid, scene.LockerUid), "it should get in");
            scene.EntManager.System<NpcRecoverySystem>().StartRecovery(scene.MobUid);

            Assert.That(IsInside(scene), "it should be inside");
            Assert.That(scene.EntManager.System<SharedEntityStorageSystem>().IsOpen(scene.LockerUid), Is.False, "and shut in");
        });

        await Pair.RunTicksSync(60);
        await Pair.Server.WaitAssertion(() => Assert.That(IsInside(scene), "still hurt, it should stay in"));

        await Pair.Server.WaitPost(() => SetDamage(scene.EntManager, scene.MobUid, 0));
        await Pair.RunTicksSync(60);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(IsInside(scene), Is.False, "healed, it should be out");
            Assert.That(scene.HidingSystem.IsHiding(scene.MobUid), Is.False);
            Assert.That(scene.EntManager.System<SharedEntityStorageSystem>().IsOpen(scene.LockerUid), "and the closet open");
        });
    }

    /// <summary>
    ///     Out at once for a hostile right outside, or for being hurt in there, meds or no meds.
    /// </summary>
    [TestCase("hostile")]
    [TestCase("hurt")]
    public async Task TestComesOutForTrouble(string trouble)
    {
        var scene = await SetUp();

        await Pair.Server.WaitPost(() =>
        {
            SetDamage(scene.EntManager, scene.MobUid, 50);
            Assert.That(scene.HidingSystem.TryHideIn(scene.MobUid, scene.LockerUid), "it should get in");
            scene.EntManager.System<NpcRecoverySystem>().StartRecovery(scene.MobUid);
        });

        await Pair.RunTicksSync(30);
        await Pair.Server.WaitAssertion(() => Assert.That(IsInside(scene), "the test needs it settled in"));

        await Pair.Server.WaitPost(() =>
        {
            if (trouble == "hostile")
                SpawnAt(scene.EntManager, NanoTrasenMob, scene.GridUid, 2, 0);
            else
                SetDamage(scene.EntManager, scene.MobUid, 60);
        });

        await Pair.RunTicksSync(30);
        await Pair.Server.WaitAssertion(() => Assert.That(IsInside(scene), Is.False, "it should come out"));
    }

    /// <summary>
    ///     Nowhere it could not get out of again by itself, and nowhere somebody already is.
    /// </summary>
    [TestCase("welded")]
    [TestCase("occupied")]
    public async Task TestNotSomewhereItCouldNotLeave(string why)
    {
        var scene = await SetUp();

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(scene.HidingSystem.IsHidingPlace(scene.LockerUid), "the control: an empty, unwelded locker will do");

            if (why == "welded")
            {
                scene.EntManager.EnsureComponent<WeldableComponent>(scene.LockerUid);
                scene.EntManager.System<Content.Shared.Tools.Systems.WeldableSystem>().SetWeldedState(scene.LockerUid, true);
            }
            else
            {
                var occupantUid = SpawnAt(scene.EntManager, SyndicateMob, scene.GridUid, -1, 0);
                Assert.That(scene.EntManager.System<SharedEntityStorageSystem>().Insert(occupantUid, scene.LockerUid), "the test needs somebody in it");
            }

            Assert.That(scene.HidingSystem.IsHidingPlace(scene.LockerUid), Is.False);
            Assert.That(scene.HidingSystem.TryHideIn(scene.MobUid, scene.LockerUid), Is.False);
            Assert.That(IsInside(scene), Is.False);
        });
    }

    /// <summary>
    ///     Meds work in a closet: what the hiding is for. A medipen used on itself from inside goes in.
    /// </summary>
    [Test]
    public async Task TestMedsWorkInside()
    {
        var scene = await SetUp(mob: "MobHuman");
        EntityUid penUid = default;

        await Pair.Server.WaitPost(() =>
        {
            penUid = SpawnAt(scene.EntManager, TestMedipen, scene.GridUid, 0, 0);
            Assert.That(scene.EntManager.System<SharedHandsSystem>().TryPickupAnyHand(scene.MobUid, penUid), "the test needs the pen in hand");
            Assert.That(scene.EntManager.System<SharedEntityStorageSystem>().Insert(scene.MobUid, scene.LockerUid), "the test needs it inside");

            var coordinates = scene.EntManager.GetComponent<TransformComponent>(scene.MobUid).Coordinates;
            scene.EntManager.System<SharedInteractionSystem>().InteractUsing(scene.MobUid, penUid, scene.MobUid, coordinates);
        });

        await Pair.RunTicksSync(120);

        await Pair.Server.WaitAssertion(() =>
        {
            var solutionSystem = scene.EntManager.System<SharedSolutionContainerSystem>();
            Assert.That(solutionSystem.TryGetSolution(penUid, "hypospray", out _, out var solution), "the test needs the pen's solution");
            Assert.That(solution!.Volume.Float(), Is.LessThan(15f), "the pen should have been used from inside the closet");
        });
    }

    private static bool IsInside(Scene scene)
    {
        return scene.EntManager.System<SharedContainerSystem>().TryGetContainingContainer(scene.MobUid, out var container) &&
            container.Owner == scene.LockerUid;
    }

    private void SetDamage(IEntityManager entManager, EntityUid mobUid, float damage)
    {
        var blunt = Pair.Server.ResolveDependency<IPrototypeManager>().Index(BluntDamage);
        entManager.System<DamageableSystem>().SetDamage(mobUid, new DamageSpecifier(blunt, damage));
    }

    private sealed record Scene(IEntityManager EntManager, NpcHidingSystem HidingSystem, EntityUid GridUid, EntityUid MobUid, EntityUid LockerUid);

    /// <summary>
    ///     A hider at 0,0 and a shut test locker beside it, on an open grid from -6,-3 to 6,3.
    /// </summary>
    private async Task<Scene> SetUp(string mob = HiderMob)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;
        EntityUid mobUid = default;
        EntityUid lockerUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-6, -3), new Vector2i(6, 3)).Owner;
            mobUid = SpawnAt(entManager, mob, gridUid, 0, 0);
            lockerUid = SpawnAt(entManager, TestLocker, gridUid, 1, 0);

            if (entManager.TryGetComponent<EntityStorageComponent>(lockerUid, out var storageComponent) && storageComponent.Open)
                entManager.System<SharedEntityStorageSystem>().CloseStorage(lockerUid);
        });

        await Pair.RunTicksSync(5);
        return new Scene(entManager, entManager.System<NpcHidingSystem>(), gridUid, mobUid, lockerUid);
    }
}
