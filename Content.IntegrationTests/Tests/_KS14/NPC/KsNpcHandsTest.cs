#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks.Operators.Interactions;
using Content.Server.NPC.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     NPCs freeing a hand to take something out (<c>NpcHandsSystem</c>), through <see cref="SwapToFreeHandOperator"/>,
///         which reloading and healing use.
/// </summary>
public sealed class KsNpcHandsTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Wieldable = "KsHandsTestWieldable";
    private const string Npc = "KsHandsTestNpc";
    private const string Gun = "KsHandsTestGun";
    private const string Magazine = "MagazineShotgun";
    private const string Medipen = "KsHandsTestMedipen";

    /// <summary>
    ///     A bare NPC, and the reloading and healing chains of the operative HTN stripped to their use of hands: free a
    ///         hand, take the thing out of the belt, use it, drop what is left, swap back to the weapon and wield it.
    /// </summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsHandsTestWieldable
  components:
  - type: Item
    size: Large
  - type: Wieldable

- type: entity
  parent: WeaponShotgunBulldog
  id: KsHandsTestGun
  components:
  - type: Wieldable

- type: entity
  parent: EmergencyMedipen
  id: KsHandsTestMedipen
  components:
  - type: NpcHealingItem

- type: entity
  id: KsHandsTestNpc
  components:
  - type: Hands
  - type: ComplexInteraction
  - type: Inventory
    templateId: human
  - type: ContainerContainer
  - type: MobState
  - type: DoAfter
  - type: SolutionManager
    solutions: null
    solutionData:
      testBlood:
        maxVol: 100
  - type: InjectableSolution
    solution: testBlood
  - type: HTN
    enabled: false
    rootTask:
      task: KsHandsTestReloadCompound

- type: htnCompound
  id: KsHandsTestReloadCompound
  branches:
  - tasks:
    - !type:HTNPrimitiveTask
      operator: !type:UtilityOperator
        proto: KsHandsTestInventoryAmmo
        key: ChosenAmmo
    - !type:HTNPrimitiveTask
      operator: !type:CopyKeyOperator
        originKey: ActiveHand
        targetKey: WeaponActiveHand
    - !type:HTNPrimitiveTask
      operator: !type:GetActiveHeldItemOperator
        key: ActiveWeapon
    - !type:HTNPrimitiveTask
      operator: !type:SwapToFreeHandOperator
    - !type:HTNPrimitiveTask
      operator: !type:EquipOperator
        target: ChosenAmmo
    - !type:HTNPrimitiveTask
      operator: !type:InteractUsingOperator
        targetKey: ActiveWeapon
    - !type:HTNPrimitiveTask
      operator: !type:DropOperator
        succeedIfHandEmpty: true
    - !type:HTNPrimitiveTask
      operator: !type:SwapToHandOperator
        key: WeaponActiveHand
    - !type:HTNPrimitiveTask
      operator: !type:WieldOperator

- type: utilityQuery
  id: KsHandsTestInventoryAmmo
  query:
  - !type:InventoryQuery
  - !type:ComponentFilter
    components:
    - type: BallisticAmmoProvider
    - type: Item
  considerations:
  - !type:TargetAmmoMatchesCon
    curve: !type:BoolCurve

- type: htnCompound
  id: KsHandsTestHealCompound
  branches:
  - tasks:
    - !type:HTNPrimitiveTask
      operator: !type:UtilityOperator
        proto: KsHandsTestInventoryMeds
        key: ChosenMeds
    - !type:HTNPrimitiveTask
      operator: !type:CopyKeyOperator
        originKey: ActiveHand
        targetKey: LastActiveHand
    - !type:HTNPrimitiveTask
      operator: !type:SwapToFreeHandOperator
    - !type:HTNPrimitiveTask
      operator: !type:EquipOperator
        target: ChosenMeds
    - !type:HTNPrimitiveTask
      operator: !type:InteractUsingOperator
        targetKey: Owner
    - !type:HTNPrimitiveTask
      operator: !type:EnsureComponentOperator
        targetKey: Owner
        components:
        - type: NPCRecentlyInjected
          removeTime: 30
    - !type:HTNPrimitiveTask
      operator: !type:DropOperator
        succeedIfHandEmpty: true
    - !type:HTNPrimitiveTask
      operator: !type:SwapToHandOperator
        key: LastActiveHand
    - !type:HTNPrimitiveTask
      operator: !type:WieldOperator

- type: utilityQuery
  id: KsHandsTestInventoryMeds
  query:
  - !type:InventoryQuery
  - !type:ComponentFilter
    components:
    - type: NpcHealingItem
    - type: Item
";

    /// <summary>
    ///     Live: an NPC with an empty gun wielded in both hands reloads it from the magazine on its belt, and ends with the
    ///         gun back in its active hand, wielded.
    /// </summary>
    [Test]
    public async Task TestReloadingWithAWieldedGun()
    {
        var (entManager, npcUid, gunUid, beltUid) = await SetUpArmedNpc();
        EntityUid magazineUid = default;

        await Pair.Server.WaitPost(() =>
        {
            magazineUid = entManager.SpawnEntity(Magazine, entManager.GetComponent<TransformComponent>(npcUid).Coordinates);
            Assert.That(entManager.System<SharedStorageSystem>().Insert(beltUid, magazineUid, out _, playSound: false), "the magazine should go in the belt");
            Start(entManager, npcUid, "KsHandsTestReloadCompound");
        });

        await RunUntil(entManager, () =>
            entManager.System<SharedContainerSystem>().TryGetContainer(gunUid, "gun_magazine", out var magazineContainer) &&
            magazineContainer.Contains(magazineUid) &&
            IsWieldedInActiveHand(entManager, npcUid, gunUid));

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entManager.System<SharedContainerSystem>().TryGetContainer(gunUid, "gun_magazine", out var magazineContainer) &&
                    magazineContainer.Contains(magazineUid), "the magazine should be in the gun");
                Assert.That(IsWieldedInActiveHand(entManager, npcUid, gunUid), "with the gun wielded in the active hand again");
            });
        });
    }

    /// <summary>
    ///     Live: an NPC with a gun wielded in both hands takes the medipen from its belt, uses it on itself, drops it, and
    ///         ends with the gun back in its active hand, wielded.
    /// </summary>
    [Test]
    public async Task TestHealingWithAWieldedGun()
    {
        var (entManager, npcUid, gunUid, beltUid) = await SetUpArmedNpc();
        EntityUid medipenUid = default;

        await Pair.Server.WaitPost(() =>
        {
            medipenUid = entManager.SpawnEntity(Medipen, entManager.GetComponent<TransformComponent>(npcUid).Coordinates);
            Assert.That(entManager.System<SharedStorageSystem>().Insert(beltUid, medipenUid, out _, playSound: false), "the medipen should go in the belt");
            Start(entManager, npcUid, "KsHandsTestHealCompound");
        });

        await RunUntil(entManager, () =>
            entManager.HasComponent<Content.Shared.NPC.Components.NPCRecentlyInjectedComponent>(npcUid) &&
            IsWieldedInActiveHand(entManager, npcUid, gunUid));

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entManager.HasComponent<Content.Shared.NPC.Components.NPCRecentlyInjectedComponent>(npcUid), "it should have used the medipen");
                Assert.That(entManager.System<Content.Shared.Chemistry.EntitySystems.SharedSolutionContainerSystem>()
                        .TryGetSolution(npcUid, "testBlood", out _, out var bloodSolution) && bloodSolution.Volume > 0,
                    "and actually injected itself, not dropped the medipen mid-injection");
                Assert.That(entManager.System<SharedHandsSystem>().IsHolding(npcUid, medipenUid), Is.False, "and dropped it");
                Assert.That(IsWieldedInActiveHand(entManager, npcUid, gunUid), "with the gun wielded in the active hand again");
            });
        });
    }

    /// <summary>
    ///     The bare NPC, wearing an empty belt, with an empty gun wielded in both hands.
    /// </summary>
    private async Task<(IEntityManager EntManager, EntityUid NpcUid, EntityUid GunUid, EntityUid BeltUid)> SetUpArmedNpc()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid npcUid = default, gunUid = default, beltUid = default;

        await server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-2, -2), new Vector2i(2, 2)).Owner;
            npcUid = SpawnAt(entManager, Npc, gridUid, 0, 0);
            var coordinates = entManager.GetComponent<TransformComponent>(npcUid).Coordinates;

            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(npcUid, "right", HandLocation.Right);
            handsSystem.AddHand(npcUid, "left", HandLocation.Left);

            beltUid = entManager.SpawnEntity("ClothingBeltMilitaryWebbing", coordinates);
            Assert.That(entManager.System<Content.Shared.Inventory.InventorySystem>().TryEquip(npcUid, beltUid, "belt", silent: true, force: true),
                "the belt should be worn");

            gunUid = entManager.SpawnEntity(Gun, coordinates);
            var containerSystem = entManager.System<SharedContainerSystem>();
            if (containerSystem.TryGetContainer(gunUid, "gun_magazine", out var magazineContainer))
            {
                foreach (var loadedUid in new System.Collections.Generic.List<EntityUid>(magazineContainer.ContainedEntities))
                {
                    entManager.DeleteEntity(loadedUid);
                }
            }

            Assert.That(handsSystem.TryPickup(npcUid, gunUid, "right"));
            handsSystem.TrySetActiveHand(npcUid, "right"); // false if it already is
            Assert.That(entManager.System<SharedWieldableSystem>().TryWield((gunUid, entManager.GetComponent<WieldableComponent>(gunUid)), npcUid),
                "the gun should be wielded, filling both hands");
        });

        await Pair.RunTicksSync(5);
        return (entManager, npcUid, gunUid, beltUid);
    }

    private static void Start(IEntityManager entManager, EntityUid npcUid, string rootTask)
    {
        var htnComponent = entManager.GetComponent<HTNComponent>(npcUid);
        htnComponent.RootTask = new HTNCompoundTask { Task = rootTask };
        entManager.System<HTNSystem>().SetHTNEnabled((npcUid, htnComponent), true);
        entManager.System<NPCSystem>().WakeNPC(npcUid, htnComponent);
    }

    private async Task RunUntil(IEntityManager entManager, System.Func<bool> condition)
    {
        var met = false;
        for (var i = 0; i < 300 && !met; i++)
        {
            await Pair.RunTicksSync(1);
            await Pair.Server.WaitPost(() => met = condition());
        }
    }

    private static bool IsWieldedInActiveHand(IEntityManager entManager, EntityUid npcUid, EntityUid itemUid)
    {
        var handsSystem = entManager.System<SharedHandsSystem>();
        return handsSystem.IsHolding(npcUid, itemUid, out var hand) &&
            hand == handsSystem.GetActiveHand(npcUid) &&
            entManager.GetComponent<WieldableComponent>(itemUid).Wielded;
    }

    /// <summary>
    ///     Both hands full with a wielded weapon: the other hand holds the wield's virtual item. Freeing a hand empties
    ///         that one in the same tick, ending the wield, so whatever is taken out next can go straight in. Swapping to
    ///         it only queued the virtual item's deletion for the end of the tick, which is why reloading and healing
    ///         used to wait a tick before taking anything out.
    /// </summary>
    [Test]
    public async Task TestFreeingAHandFromAWield()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        var swapOperator = new SwapToFreeHandOperator();
        EntityUid npcUid = default, weaponUid = default;

        await server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-2, -2), new Vector2i(2, 2)).Owner;
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(swapOperator, oneOff: true);

            npcUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
            var handsSystem = entManager.System<SharedHandsSystem>();
            entManager.EnsureComponent<HandsComponent>(npcUid);
            handsSystem.AddHand(npcUid, "right", HandLocation.Right);
            handsSystem.AddHand(npcUid, "left", HandLocation.Left);

            weaponUid = entManager.SpawnEntity(Wieldable, new EntityCoordinates(gridUid, new Vector2(0.5f, 0.5f)));
            Assert.That(handsSystem.TryPickup(npcUid, weaponUid, "right"));
            handsSystem.TrySetActiveHand(npcUid, "right"); // false if it already is
            Assert.That(entManager.System<SharedWieldableSystem>().TryWield((weaponUid, entManager.GetComponent<WieldableComponent>(weaponUid)), npcUid),
                "the weapon should be wielded, filling both hands");
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var handsSystem = entManager.System<SharedHandsSystem>();
            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, npcUid);

            Assert.That(swapOperator.Update(blackboard, 1f / 30f), Is.EqualTo(HTNOperatorStatus.Finished), "a hand should be freed");

            var activeHand = handsSystem.GetActiveHand(npcUid);
            Assert.Multiple(() =>
            {
                Assert.That(activeHand, Is.EqualTo("left"), "the hand the wield was holding should be the active one");
                Assert.That(handsSystem.TryGetHeldItem(npcUid, activeHand!, out _), Is.False, "and empty now, not at the end of the tick");
                Assert.That(entManager.GetComponent<WieldableComponent>(weaponUid).Wielded, Is.False, "the wield is over");
                Assert.That(handsSystem.IsHolding(npcUid, weaponUid), "but the weapon still held");
            });
        });
    }
}
