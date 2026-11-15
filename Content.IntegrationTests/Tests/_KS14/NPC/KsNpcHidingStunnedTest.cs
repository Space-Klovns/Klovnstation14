#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Hiding;
using Content.Server._KS14.NPC.Recovery;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Stunnable;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     <see cref="NpcHidingSystem"/>: an NPC hiding in a closet that cannot act (stunned) is not let out by a hostile coming
///         right up to it, as it would be otherwise. It comes out for it once it can act again.
/// </summary>
public sealed class KsNpcHidingStunnedTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly ProtoId<DamageTypePrototype> BluntDamage = "Blunt";

    private const string HiderMob = "KsHidingStunnedTestMob";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsHidingStunnedTestMob
  components:
  - type: Hands
  - type: DoAfter
  - type: NpcRecovery
    maxDuration: 1000
  - type: NpcClosetHealer
";

    [Test]
    public async Task TestStunnedHiderStaysInWithAHostileOutside()
    {
        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var hidingSystem = entManager.System<NpcHidingSystem>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, mobUid = default, lockerUid = default;

        bool IsInside() => entManager.System<SharedContainerSystem>().TryGetContainingContainer(mobUid, out var container) &&
            container.Owner == lockerUid;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-6, -3), new Vector2i(6, 3)).Owner;
            mobUid = SpawnAt(entManager, HiderMob, gridUid, 0, 0);
            lockerUid = SpawnAt(entManager, TestLocker, gridUid, 1, 0);

            if (entManager.TryGetComponent<EntityStorageComponent>(lockerUid, out var storageComponent) && storageComponent.Open)
                entManager.System<SharedEntityStorageSystem>().CloseStorage(lockerUid);
        });

        await Pair.RunTicksSync(5);

        await Server.WaitPost(() =>
        {
            // Hurt, and its meds at work: nothing but the hostile is a reason to come out.
            var blunt = Server.ResolveDependency<IPrototypeManager>().Index(BluntDamage);
            entManager.System<DamageableSystem>().SetDamage(mobUid, new DamageSpecifier(blunt, 50));
            Assert.That(hidingSystem.TryHideIn(mobUid, lockerUid), "it should get in");
            entManager.System<NpcRecoverySystem>().StartRecovery(mobUid);
        });

        await Pair.RunTicksSync(30);
        await Server.WaitAssertion(() => Assert.That(IsInside(), "the test needs it settled in"));

        await Server.WaitPost(() =>
        {
            entManager.EnsureComponent<StunnedComponent>(mobUid);
            SpawnAt(entManager, NanoTrasenMob, gridUid, 2, 0);
        });

        await Pair.RunTicksSync(30);
        await Server.WaitAssertion(() =>
        {
            Assert.That(IsInside(), "stunned, it should stay in the closet, hostile or no hostile");
            Assert.That(hidingSystem.IsHiding(mobUid), "and still be hiding");
        });

        await Server.WaitPost(() => entManager.RemoveComponent<StunnedComponent>(mobUid));

        await Pair.RunTicksSync(30);
        await Server.WaitAssertion(() => Assert.That(IsInside(), Is.False, "able to act again, it should come out for the hostile"));
    }
}
