#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.NPC.HTN;
using Content.Shared.Storage.EntitySystems;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Getting into a closet to hide (<c>EnterEntityStorageOperator</c>): the NPC opens it, steps onto it and shuts it,
///         as anyone does, rather than being put in from where it stood - seen to jump into a shut closet. And not with
///         a hostile in earshot, which would have it straight back out again.
/// </summary>
[TestFixture]
public sealed class KsNpcHidingStepInTest : GameTest
{
    // Connected: a real NPC only runs while a player is about.
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: htnCompound
  id: KsHidingStepInTestCompound
  branches:
  - preconditions:
    - !type:KeyExistsPrecondition
      key: HealCloset
    tasks:
    - !type:HTNPrimitiveTask
      operator: !type:EnterEntityStorageOperator
        targetKey: HealCloset

- type: entity
  parent: MobHuman
  id: KsHidingStepInTestMob
  components:
  - type: HTN
    rootTask:
      task: KsHidingStepInTestCompound
  - type: NpcClosetHealer
  - type: NpcFactionMember
    factions:
    - Syndicate
";

    [Test]
    public async Task TestStepsInRatherThanJumpingIn([Values] bool hostileInEarshot)
    {
        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var containerSystem = entManager.System<SharedContainerSystem>();
        var storageSystem = entManager.System<SharedEntityStorageSystem>();
        var map = await Pair.CreateTestMap();
        EntityUid mobUid = default, lockerUid = default;

        await Server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-6, -3), new Vector2i(6, 3)).Owner;
            GiveGravity(entManager, gridUid);
            mobUid = SpawnAt(entManager, "KsHidingStepInTestMob", gridUid, 0, 0);
            entManager.RemoveComponent<Content.Server.Atmos.Components.BarotraumaComponent>(mobUid);
            entManager.RemoveComponent<Content.Server.Body.Components.RespiratorComponent>(mobUid);
            lockerUid = SpawnAt(entManager, TestLocker, gridUid, 1, 0);
            if (storageSystem.IsOpen(lockerUid))
                storageSystem.CloseStorage(lockerUid);

            if (hostileInEarshot)
                SpawnAt(entManager, NanoTrasenMob, gridUid, -1, 0);
        });

        await Pair.RunTicksSync(5);
        await Server.WaitPost(() => entManager.GetComponent<HTNComponent>(mobUid).Blackboard.SetValue("HealCloset", lockerUid));

        var inside = false;
        var openedFirst = false;
        var distanceWhenIn = float.MaxValue;
        var lastDistance = float.MaxValue;
        var log = new System.Text.StringBuilder();

        for (var tick = 0; tick < 300 && !inside; tick++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() =>
            {
                inside = containerSystem.TryGetContainingContainer(mobUid, out var container) && container.Owner == lockerUid;
                if (inside)
                {
                    distanceWhenIn = lastDistance;
                    return;
                }

                openedFirst |= storageSystem.IsOpen(lockerUid);
                lastDistance = (transformSystem.GetWorldPosition(mobUid) - transformSystem.GetWorldPosition(lockerUid)).Length();
                var plan = entManager.GetComponent<HTNComponent>(mobUid).Plan;
                log.AppendLine($"+{tick}: {plan?.CurrentOperator.GetType().Name} open={storageSystem.IsOpen(lockerUid)} d={lastDistance:0.00} pos={transformSystem.GetWorldPosition(mobUid)}");
            });
        }

        if (inside == hostileInEarshot)
            TestContext.Out.WriteLine(log.ToString());

        if (hostileInEarshot)
        {
            Assert.That(inside, Is.False, "with a hostile in earshot, it should not hide: it would come straight back out");
            return;
        }

        Assert.Multiple(() =>
        {
            Assert.That(inside, "it should get in");
            Assert.That(openedFirst, "having opened it first");
            Assert.That(distanceWhenIn, Is.LessThan(0.5f), "from standing on it, not from a step away");
        });
    }
}
