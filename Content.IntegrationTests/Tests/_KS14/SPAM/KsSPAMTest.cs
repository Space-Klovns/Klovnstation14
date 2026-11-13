#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.SPAM;
using Content.Shared._KS14.SPAM;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Storage.EntitySystems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.SPAM;

[TestFixture]
[TestOf(typeof(KsSPAMSystem))]
public sealed class KsSPAMTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId SalvageId = "SalvageIDCard";
    private static readonly EntProtoId PassengerId = "PassengerIDCard";
    private readonly EntProtoId _tablet = "KsTestSPAM";
    private readonly EntProtoId _allDealtTablet = "KsTestSPAMAllDealt";
    private readonly EntProtoId _fauna = "KsTestSPAMFauna";
    private const string ScanTask = "KsTestSPAMScanTask";
    private const string OreTask = "KsTestSPAMOreTask";
    private static readonly EntProtoId OreBag = "OreBag";
    private static readonly EntProtoId IronOre = "SteelOre1";
    private const string HighRankTask = "KsTestSPAMHighRankTask";
    private const int ScanTaskPoints = 70;
    private const int ScanTaskReputation = 4;

    // The scan task outweighs every real task, so an account is always dealt it.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSPAM
  id: KsTestSPAM
  components:
  - type: KsSPAM
    dropCooldown: 5

# More slots than there are tasks, so every task on offer is dealt: what a new salvager has, at rank 0.
- type: entity
  parent: KsTestSPAM
  id: KsTestSPAMAllDealt
  components:
  - type: KsSPAM
    taskSlots: 50

- type: entity
  parent: MobGoliath
  id: KsTestSPAMFauna

- type: ksSPAMTask
  id: KsTestSPAMScanTask
  name: ks-spam-task-goliath
  icon: MobGoliath
  objective: !type:KsSPAMCorpseObjective
    prototypes: [ KsTestSPAMFauna ]
  minAmount: 2
  maxAmount: 2
  pointsPerUnit: 35
  reputation: 4
  duration: 20
  weight: 1000000000

- type: ksSPAMTask
  id: KsTestSPAMOreTask
  name: ks-spam-task-iron
  icon: SteelOre1
  objective: !type:KsSPAMOreObjective
    stack: SteelOre
  minAmount: 100
  maxAmount: 100
  duration: 5
  weight: 1000000000

- type: ksSPAMTask
  id: KsTestSPAMSmallOreTask
  name: ks-spam-task-iron
  icon: SteelOre1
  objective: !type:KsSPAMOreObjective
    stack: SteelOre
  minAmount: 2
  maxAmount: 2
  weight: 1000000000

- type: ksSPAMTask
  id: KsTestSPAMHighRankTask
  name: ks-spam-task-goliath
  icon: MobGoliath
  objective: !type:KsSPAMCorpseObjective
    prototypes: [ KsTestSPAMFauna ]
  minRank: 99
  weight: 1000000000
";

    private sealed class Scene
    {
        public EntityUid UserUid;
        public EntityUid TabletUid;
        public EntityUid CardUid;
        public EntityCoordinates Coordinates;
    }

    /// <summary>
    ///     A human holding a tablet in one hand and an ID in the other.
    /// </summary>
    private async Task<Scene> SetUpAsync(EntProtoId card, EntProtoId? tablet = null)
    {
        var testMap = await Pair.CreateTestMap();
        var scene = new Scene { Coordinates = new EntityCoordinates(testMap.Grid, 0.5f, 0.5f) };
        await Server.WaitPost(() =>
        {
            var handsSystem = SEntMan.System<SharedHandsSystem>();
            scene.UserUid = SEntMan.SpawnEntity(Human, scene.Coordinates);
            scene.CardUid = SEntMan.SpawnEntity(card, scene.Coordinates);
            scene.TabletUid = SEntMan.SpawnEntity(tablet ?? _tablet, scene.Coordinates);
            // The tablet first, so it is in the active hand, as it has to be to open or to scan.
            Assert.That(handsSystem.TryPickupAnyHand(scene.UserUid, scene.TabletUid), Is.True);
            Assert.That(handsSystem.TryPickupAnyHand(scene.UserUid, scene.CardUid), Is.True);
        });
        await Pair.RunTicksSync(5);
        return scene;
    }

    private Entity<KsSPAMComponent> Tablet(Scene scene) => (scene.TabletUid, SEntMan.GetComponent<KsSPAMComponent>(scene.TabletUid));

    private KsMinerAccountComponent Account(Scene scene) => SEntMan.GetComponent<KsMinerAccountComponent>(scene.CardUid);

    private readonly ProtoId<KsSPAMTaskPrototype> _scanTaskId = ScanTask;
    private readonly ProtoId<KsSPAMTaskPrototype> _oreTaskId = OreTask;

    private int TicksFor(float seconds) => (int)(seconds * (float)Server.ResolveDependency<IGameTiming>().TickRate);

    private async Task<EntityUid> SpawnFaunaAsync(Scene scene, bool dead)
    {
        var faunaUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            faunaUid = SEntMan.SpawnEntity(_fauna, scene.Coordinates);
            if (dead)
                SEntMan.System<MobStateSystem>().ChangeMobState(faunaUid, MobState.Dead);
        });
        await Pair.RunTicksSync(2);
        return faunaUid;
    }

    /// <summary>
    ///     Uses the tablet on a target and waits out the scan.
    /// </summary>
    private async Task ScanAsync(Scene scene, EntityUid targetUid)
    {
        await Server.WaitPost(() =>
        {
            SEntMan.System<SharedInteractionSystem>().InteractUsing(scene.UserUid, scene.TabletUid, targetUid, scene.Coordinates);
        });
        await Pair.RunTicksSync(TicksFor(3f));
    }

    [Test]
    public async Task LoginTakesASalvageIdAndDealsTasks()
    {
        var scene = await SetUpAsync(SalvageId);

        await Server.WaitAssertion(() =>
        {
            var spamSystem = SEntMan.System<KsSPAMSystem>();
            Assert.That(spamSystem.TryLogin(Tablet(scene), scene.UserUid), Is.True, "A salvage ID must log in.");
            Assert.That(Tablet(scene).Comp.BoundCard, Is.EqualTo(scene.CardUid));

            var tasks = Account(scene).Tasks;
            Assert.That(Tablet(scene).Comp.TaskSlots, Is.EqualTo(6), "A tablet offers six tasks.");
            Assert.That(tasks, Has.Count.EqualTo(Tablet(scene).Comp.TaskSlots), "Every task slot must be dealt.");
            Assert.That(tasks.Any(task => task.Tracked || task.EndTime != null), Is.False, "A dealt task must wait to be tracked before its time runs.");
            Assert.That(tasks.Select(task => task.Task.Id).Distinct().Count(), Is.EqualTo(tasks.Count), "No task may be dealt twice.");
            Assert.That(tasks.Any(task => task.Task.Id == ScanTask), Is.True, "The heavy test task was not dealt, so weights do nothing.");
            Assert.That(tasks.Any(task => task.Task.Id == HighRankTask), Is.False, "A task above the account's rank must not be dealt.");
        });
    }

    [Test]
    public async Task LoginRefusesAnIdWithoutSalvageAccess()
    {
        var scene = await SetUpAsync(PassengerId);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<KsSPAMSystem>().TryLogin(Tablet(scene), scene.UserUid), Is.False);
            Assert.That(Tablet(scene).Comp.BoundCard, Is.Null);
            Assert.That(SEntMan.HasComponent<KsMinerAccountComponent>(scene.CardUid), Is.False);
        });
    }

    [Test]
    public async Task DroppingReplacesTheTaskAndStartsACooldown()
    {
        var scene = await SetUpAsync(SalvageId);

        ProtoId<KsSPAMTaskPrototype> AnyOtherTask() => Account(scene).Tasks.First(task => task.Task.Id != ScanTask && task.Task.Id != OreTask).Task;

        await Server.WaitAssertion(() =>
        {
            var spamSystem = SEntMan.System<KsSPAMSystem>();
            spamSystem.TryLogin(Tablet(scene), scene.UserUid);

            var dropped = AnyOtherTask();
            Assert.That(spamSystem.TryDropTask(Tablet(scene), dropped), Is.True);
            Assert.That(Account(scene).Tasks.Any(task => task.Task == dropped), Is.False, "A dropped task must be replaced by a different one.");
            Assert.That(Account(scene).Tasks, Has.Count.EqualTo(Tablet(scene).Comp.TaskSlots), "A dropped task must be replaced.");
            Assert.That(spamSystem.TryDropTask(Tablet(scene), AnyOtherTask()), Is.False, "A second drop must wait for the cooldown.");
        });

        await Pair.RunTicksSync(TicksFor(6f));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<KsSPAMSystem>().TryDropTask(Tablet(scene), AnyOtherTask()), Is.True, "The cooldown must end.");
        });
    }

    /// <summary>
    ///     With every task on offer already dealt there is nothing different to deal in its place. The drop used to deal
    ///         the same task back, so the X looked like it did nothing.
    /// </summary>
    [Test]
    public async Task DroppingWithNothingElseOnOfferEmptiesTheSlotUntilTheCooldownEnds()
    {
        var scene = await SetUpAsync(SalvageId, tablet: _allDealtTablet);
        var dealt = 0;

        await Server.WaitAssertion(() =>
        {
            var spamSystem = SEntMan.System<KsSPAMSystem>();
            Assert.That(spamSystem.TryLogin(Tablet(scene), scene.UserUid), Is.True);
            dealt = Account(scene).Tasks.Count;
            Assert.That(dealt, Is.LessThan(Tablet(scene).Comp.TaskSlots), "precondition: every task on offer is dealt");

            Assert.That(spamSystem.TryTrackTask(Tablet(scene), _oreTaskId), Is.True);
            spamSystem.Advance<KsSPAMOreObjective>(new Entity<KsMinerAccountComponent?>(scene.CardUid, Account(scene)), _ => true, amount: 5);

            Assert.That(spamSystem.TryDropTask(Tablet(scene), _oreTaskId), Is.True, "A drop must work when nothing else is on offer.");
            Assert.That(Account(scene).Tasks.Any(task => task.Task == _oreTaskId), Is.False, "A dropped task must be gone.");
            Assert.That(Account(scene).Tasks, Has.Count.EqualTo(dealt - 1), "Nothing must take the dropped task's place yet.");
        });

        await Pair.RunTicksSync(TicksFor(7f));

        await Server.WaitAssertion(() =>
        {
            Assert.That(Account(scene).Tasks, Has.Count.EqualTo(dealt), "The slot must be dealt again when the cooldown ends.");
            var redealt = Account(scene).Tasks.Single(task => task.Task == _oreTaskId);
            Assert.That(redealt.Tracked, Is.False, "The task dealt again must start untracked.");
            Assert.That(redealt.Progress, Is.Zero, "The task dealt again must start from nothing.");
        });
    }

    [Test]
    public async Task OnlySoManyTasksAreTrackedAndOnlyThoseCount()
    {
        var scene = await SetUpAsync(SalvageId);

        await Server.WaitAssertion(() =>
        {
            var spamSystem = SEntMan.System<KsSPAMSystem>();
            var timing = Server.ResolveDependency<IGameTiming>();
            spamSystem.TryLogin(Tablet(scene), scene.UserUid);
            var account = new Entity<KsMinerAccountComponent?>(scene.CardUid, Account(scene));

            Assert.That(spamSystem.Advance<KsSPAMOreObjective>(account, _ => true), Is.False, "An untracked task must not count progress.");
            Assert.That(Account(scene).Tasks.All(task => task.Progress == 0), Is.True);

            Assert.That(spamSystem.TryTrackTask(Tablet(scene), _oreTaskId), Is.True);
            var oreTask = Account(scene).Tasks.First(task => task.Task == _oreTaskId);
            Assert.That(oreTask.Tracked, Is.True);
            Assert.That(oreTask.EndTime, Is.EqualTo(timing.CurTime + TimeSpan.FromSeconds(5)), "The time must start running when the task is tracked.");
            Assert.That(spamSystem.TryTrackTask(Tablet(scene), _oreTaskId), Is.False, "A tracked task can't be tracked again.");

            Assert.That(spamSystem.Advance<KsSPAMOreObjective>(account, _ => true), Is.True, "A tracked task must count progress.");
            Assert.That(oreTask.Progress, Is.EqualTo(1));

            Assert.That(Tablet(scene).Comp.MaxTrackedTasks, Is.EqualTo(2));
            Assert.That(spamSystem.TryTrackTask(Tablet(scene), _scanTaskId), Is.True);
            var third = Account(scene).Tasks.First(task => !task.Tracked).Task;
            Assert.That(spamSystem.TryTrackTask(Tablet(scene), third), Is.False, "No more than two tasks may be tracked at once.");
        });
    }

    [Test]
    public async Task OnlyATrackedTaskRunsOutOfTime()
    {
        var scene = await SetUpAsync(SalvageId);

        await Server.WaitAssertion(() =>
        {
            var spamSystem = SEntMan.System<KsSPAMSystem>();
            spamSystem.TryLogin(Tablet(scene), scene.UserUid);
            Assert.That(spamSystem.TryTrackTask(Tablet(scene), _scanTaskId), Is.True);
        });

        // The scan task lasts 20 seconds once tracked. The ore task would last 5, and is left untracked.
        await Pair.RunTicksSync(TicksFor(22f));

        await Server.WaitAssertion(() =>
        {
            var tasks = Account(scene).Tasks;
            Assert.That(tasks, Has.Count.EqualTo(Tablet(scene).Comp.TaskSlots), "A failed task must be replaced.");
            Assert.That(tasks.Any(task => task.Tracked), Is.False, "A tracked task that ran out of time must be gone.");
            Assert.That(tasks.Any(task => task.Task == _oreTaskId), Is.True, "An untracked task must not run out of time.");
        });
    }

    [Test]
    public async Task ScanningBodiesFinishesTheTaskAndPays()
    {
        var scene = await SetUpAsync(SalvageId);
        await Server.WaitPost(() =>
        {
            SEntMan.System<KsSPAMSystem>().TryLogin(Tablet(scene), scene.UserUid);
            SEntMan.System<KsSPAMSystem>().TryTrackTask(Tablet(scene), _scanTaskId);
        });

        int Progress() => Account(scene).Tasks.First(task => task.Task.Id == ScanTask).Progress;

        // Spawned, scanned and removed in one go: a living goliath would maul the scanner within the second.
        await Server.WaitAssertion(() =>
        {
            var livingUid = SEntMan.SpawnEntity(_fauna, scene.Coordinates);
            SEntMan.System<SharedInteractionSystem>().InteractUsing(scene.UserUid, scene.TabletUid, livingUid, scene.Coordinates);
            Assert.That(SEntMan.GetComponent<DoAfterComponent>(scene.UserUid).DoAfters, Is.Empty, "A living creature must not be scanned.");
            SEntMan.DeleteEntity(livingUid);
        });

        var firstBodyUid = await SpawnFaunaAsync(scene, dead: true);
        await ScanAsync(scene, firstBodyUid);
        await Server.WaitAssertion(() => Assert.That(Progress(), Is.EqualTo(1), "A dead creature the task wants must count."));

        await ScanAsync(scene, firstBodyUid);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Progress(), Is.EqualTo(1), "A body must count once.");
            Assert.That(Account(scene).Points, Is.Zero);
        });

        var secondBodyUid = await SpawnFaunaAsync(scene, dead: true);
        await ScanAsync(scene, secondBodyUid);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Account(scene).Points, Is.EqualTo(ScanTaskPoints), "Finishing the task must pay its mining points.");
            Assert.That(Account(scene).Reputation, Is.EqualTo(ScanTaskReputation), "Finishing the task must pay its reputation.");
            Assert.That(Account(scene).Tasks, Has.Count.EqualTo(Tablet(scene).Comp.TaskSlots), "A finished task must be replaced.");
        });
    }

    /// <summary>
    ///     Ore counts as it enters a bag that has the tablet attached, and each unit only once.
    /// </summary>
    [Test]
    public async Task OreInABagWithTheTabletCountsOnce()
    {
        var scene = await SetUpAsync(SalvageId);
        var bagUid = EntityUid.Invalid;
        var firstOreUid = EntityUid.Invalid;

        int Progress() => Account(scene).Tasks.First(task => task.Task.Id == OreTask).Progress;

        await Server.WaitAssertion(() =>
        {
            var storageSystem = SEntMan.System<SharedStorageSystem>();
            SEntMan.System<KsSPAMSystem>().TryLogin(Tablet(scene), scene.UserUid);
            SEntMan.System<KsSPAMSystem>().TryTrackTask(Tablet(scene), _oreTaskId);

            bagUid = SEntMan.SpawnEntity(OreBag, scene.Coordinates);
            firstOreUid = SEntMan.SpawnEntity(IronOre, scene.Coordinates);
            Assert.That(storageSystem.Insert(bagUid, firstOreUid, out _), Is.True);
            Assert.That(Progress(), Is.Zero, "Ore must not count while no tablet is attached to the bag.");

            Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(bagUid, "ks_spam", scene.TabletUid, scene.UserUid), Is.True,
                "The tablet must fit the bag's slot.");

            // Merges into the stack already in the bag.
            Assert.That(storageSystem.Insert(bagUid, SEntMan.SpawnEntity(IronOre, scene.Coordinates), out _), Is.True);
            Assert.That(Progress(), Is.EqualTo(1), "Ore merged into a stack in the bag must count.");

            // Out and back in: only the unit that was bagged before the tablet is still uncounted.
            SEntMan.System<SharedTransformSystem>().DropNextTo(firstOreUid, bagUid);
            Assert.That(storageSystem.Insert(bagUid, firstOreUid, out _), Is.True);
            Assert.That(Progress(), Is.EqualTo(2), "Ore put in the bag as its own stack must count its uncounted units.");

            SEntMan.System<SharedTransformSystem>().DropNextTo(firstOreUid, bagUid);
            Assert.That(storageSystem.Insert(bagUid, firstOreUid, out _), Is.True);
            Assert.That(Progress(), Is.EqualTo(2), "Ore that was counted must not count again.");
        });
    }

    /// <summary>
    ///     Ore that finished a task stays counted for the task dealt in its place.
    /// </summary>
    [Test]
    public async Task OreThatFinishedATaskDoesNotCountForTheNext()
    {
        var scene = await SetUpAsync(SalvageId, _allDealtTablet);
        ProtoId<KsSPAMTaskPrototype> smallTask = "KsTestSPAMSmallOreTask";

        await Server.WaitAssertion(() =>
        {
            var spamSystem = SEntMan.System<KsSPAMSystem>();
            var storageSystem = SEntMan.System<SharedStorageSystem>();
            var transformSystem = SEntMan.System<SharedTransformSystem>();
            Assert.That(spamSystem.TryLogin(Tablet(scene), scene.UserUid), Is.True);
            Assert.That(spamSystem.TryTrackTask(Tablet(scene), smallTask), Is.True);

            var bagUid = SEntMan.SpawnEntity(OreBag, scene.Coordinates);
            Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(bagUid, "ks_spam", scene.TabletUid, scene.UserUid), Is.True);

            var oreUid = SEntMan.SpawnEntity(IronOre, scene.Coordinates);
            Assert.That(storageSystem.Insert(bagUid, oreUid, out _), Is.True);
            Assert.That(storageSystem.Insert(bagUid, SEntMan.SpawnEntity(IronOre, scene.Coordinates), out _), Is.True);
            var next = Account(scene).Tasks.FirstOrDefault(task => task.Task == smallTask);
            Assert.That(next, Is.Not.Null, "The finished task must be dealt again.");
            Assert.That(next!.Tracked, Is.False);
            Assert.That(spamSystem.TryTrackTask(Tablet(scene), smallTask), Is.True);

            foreach (var stored in SEntMan.GetComponent<Content.Shared.Storage.StorageComponent>(bagUid).Container.ContainedEntities.ToArray())
            {
                transformSystem.DropNextTo(stored, bagUid);
                Assert.That(storageSystem.Insert(bagUid, stored, out _), Is.True);
            }

            Assert.That(Account(scene).Tasks.First(task => task.Task == smallTask).Progress, Is.Zero,
                "Ore that finished a task must not count again for the next one.");
        });
    }
}
