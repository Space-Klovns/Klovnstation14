#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks.Operators.Interactions;
using Content.Shared.Hands.Components;
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

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsHandsTestWieldable
  components:
  - type: Item
    size: Large
  - type: Wieldable
";

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
