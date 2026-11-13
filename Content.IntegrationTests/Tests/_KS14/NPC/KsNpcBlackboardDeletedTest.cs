#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.HTN;
using Content.Server.NPC;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     The blackboard never hands out an entity, or coordinates on one, that is deleted or being deleted: an operator
///         passing a gibbed target on to a system (<c>EnsureComp</c>) threw, every tick, and took the NPC update with it.
/// </summary>
public sealed class KsNpcBlackboardDeletedTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [Test]
    public async Task TestDeletedEntitiesReadAsMissing()
    {
        var map = await Pair.CreateTestMap();
        var entManager = Pair.Server.ResolveDependency<IEntityManager>();

        await Pair.Server.WaitAssertion(() =>
        {
            var targetUid = entManager.SpawnEntity(null, map.GridCoords);
            var blackboard = new NPCBlackboard();
            blackboard.SetValue("Target", targetUid);
            blackboard.SetValue("TargetCoordinates", new EntityCoordinates(targetUid, 0, 0));
            blackboard.SetValue("Spot", map.GridCoords);
            blackboard.SetValue("Nothing", EntityUid.Invalid);

            Assert.Multiple(() =>
            {
                Assert.That(blackboard.TryGetEntity("Target", entManager, out _), "alive: there");
                Assert.That(blackboard.TryGetValue<EntityCoordinates>("TargetCoordinates", out _, entManager), "on it: there");
            });

            entManager.DeleteEntity(targetUid);

            Assert.Multiple(() =>
            {
                Assert.That(blackboard.TryGetEntity("Target", entManager, out _), Is.False, "deleted: missing");
                Assert.That(blackboard.TryGetValue<EntityCoordinates>("TargetCoordinates", out _, entManager), Is.False,
                    "coordinates on a deleted entity: missing");
                Assert.That(blackboard.GetValueOrDefault<EntityUid>("Target", entManager), Is.EqualTo(default(EntityUid)));
                Assert.That(blackboard.TryGetValue<EntityCoordinates>("Spot", out _, entManager), "on the grid: still there");
                Assert.That(blackboard.TryGetValue<EntityUid>("Nothing", out _, entManager), "invalid on purpose: as ever");
            });
        });
    }
}
