#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Recovery;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Shared._KS14.NPC;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     A hunt softening a room (<c>NpcSquadTacticsSystem.Soften.cs</c>) minds the members waiting for their meds to work, as a
///         squad plan does: they are left out of the hunt, and still stand where a grenade may land. Tactics are driven by
///         hand, and what each member believes is staged.
/// </summary>
public sealed class KsNpcHuntSoftenRecoveringTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Mob = "KsHuntSoftenTestMob";
    private const string Frag = "KsHuntSoftenTestFrag";

    [TestPrototypes]
    private const string Prototypes = @"
# Plans, hunts the moment it loses a hostile, and, where it could put in either, a grenade before smoke: nothing left to chance.
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsHuntSoftenTestMob
  components:
  - type: Hands
  - type: DoAfter
  - type: NpcRecovery
  - type: NpcSquadMember
    tactics:
      planning: true
      planGrace: 0s
      stageTimeout: 0s
      maxStageDistance: 40
      watchTime: 0s
      planSmokeOverFragChance: 0

- type: entity
  id: KsHuntSoftenTestFrag
  components:
  - type: Item
  - type: Explosive
    explosionType: Default
    totalIntensity: 10
    intensitySlope: 3
    maxIntensity: 10
  - type: TimerTrigger
    delay: 1000
";

    /// <summary>
    ///     A squad of three outside a room it lost an armed hostile in, one of them waiting for its meds to work and another
    ///         with a grenade. With the one recovering out of the blast, the grenade goes in ahead; with it standing where the
    ///         grenade would land, it does not.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task TestAHuntsGrenadeMindsARecoveringMember(bool recoveringByTheLanding)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
        var map = await Pair.CreateTestMap();

        EntityUid gridUid = default, firstUid = default, secondUid = default, thirdUid = default, targetUid = default, doorUid = default;

        await server.WaitPost(() =>
        {
            tacticsSystem.UpdatesPaused = true;
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-12, -12), new Vector2i(12, 12)).Owner;

            // A walled room from 0,0 to 6,5, with a door in its west wall.
            for (var x = -1; x <= 7; x++)
            {
                for (var y = -1; y <= 6; y++)
                {
                    if (x is > -1 and < 7 && y is > -1 and < 6)
                        continue;

                    if (x == -1 && y == 2)
                        doorUid = SpawnPoweredDoorAt(entManager, TestSecurityAirlock, gridUid, x, y);
                    else
                        SpawnAt(entManager, TestWall, gridUid, x, y);
                }
            }

            firstUid = SpawnAt(entManager, Mob, gridUid, -3, 3);
            secondUid = SpawnAt(entManager, Mob, gridUid, -3, 2);
            thirdUid = SpawnAt(entManager, Mob, gridUid, -4, 2);
            targetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 3, 3);
        });

        await Pair.RunTicksSync(150); // for the squad to form

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.System<NpcSquadSystem>().TryGetSquad(firstUid, out var squadEntity), "they should form a squad");
            var squadUid = squadEntity!.Value.Owner;
            Assert.That(squadEntity.Value.Comp.Members, Has.Count.EqualTo(3), "the test needs all three in one squad");

            // Two to stack up, as one on its own just goes in, and a third waiting for its meds.
            var throwerUid = squadEntity.Value.Comp.Leader!.Value;
            var recoveringUid = throwerUid == firstUid ? secondUid : firstUid;
            Assert.That(thirdUid, Is.Not.EqualTo(recoveringUid));

            // Gone, so there is a way to throw something through: the doorway is still known for one.
            entManager.DeleteEntity(doorUid);

            var transformSystem = entManager.System<SharedTransformSystem>();
            var handsSystem = entManager.System<SharedHandsSystem>();
            var handId = $"hand{entManager.GetComponent<HandsComponent>(throwerUid).Count}";
            handsSystem.AddHand(throwerUid, handId, HandLocation.Right);
            var fragUid = entManager.SpawnAtPosition(Frag, entManager.GetComponent<TransformComponent>(throwerUid).Coordinates);
            Assert.That(handsSystem.TryPickup(throwerUid, fragUid, handId), "the test needs it holding a grenade");

            // Lost a moment ago, in the room, last seen with a gun.
            var now = IoCManager.Resolve<IGameTiming>().CurTime;
            var believed = transformSystem.GetMoverCoordinates(targetUid);
            entManager.System<NpcPerceptionSystem>().SetContact(throwerUid, targetUid, new NpcContact(NpcContactState.Lost,
                now, now, now, believed, default, null, Reacted: true, ReactAt: now, ObserverWasMoving: false,
                Traits: NpcContactTraits.Seen | NpcContactTraits.Armed));

            entManager.System<NpcRecoverySystem>().StartRecovery(recoveringUid);
            Assert.That(entManager.System<NpcRecoverySystem>().IsRecovering(recoveringUid), "the test needs one waiting for its meds");

            // Where the grenade would land: by where the hostile is believed to be.
            if (recoveringByTheLanding)
                transformSystem.SetCoordinates(recoveringUid, believed.Offset(new System.Numerics.Vector2(1f, 1f)));

            tacticsSystem.UpdateNow();
            var hunt = tacticsSystem.GetHunt(squadUid);
            Assert.That(hunt, Is.Not.Null, "the test needs them hunting");
            Assert.That(hunt!.StagedMembers, Is.Not.Empty, "the test needs the one with the grenade stacked up on the room");

            for (var i = 0; i < 3 && hunt.Phase is NpcHuntPhase.Stage or NpcHuntPhase.Breach; i++)
            {
                tacticsSystem.UpdateNow();
            }

            Assert.That(hunt.SoftenTried, "the test needs them to have got as far as going in");

            if (recoveringByTheLanding)
            {
                Assert.That(hunt.Phase, Is.Not.EqualTo(NpcHuntPhase.Soften), "no grenade goes in on top of a squadmate");
                Assert.That(tacticsSystem.TryGetOrder(throwerUid, out var noThrow) && noThrow.Kind == NpcOrderKind.ThrowGrenade, Is.False);
                return;
            }

            Assert.That(hunt.Phase, Is.EqualTo(NpcHuntPhase.Soften), "with everyone out of the blast, the grenade goes in ahead");
            Assert.That(hunt.SoftenAction, Is.EqualTo(NpcSquadAction.FragRoom));
            Assert.That(tacticsSystem.TryGetOrder(throwerUid, out var order) && order.Kind == NpcOrderKind.ThrowGrenade,
                "the one with the grenade should be told to throw it");
        });
    }
}
