#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.HTN.Preconditions;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Combat;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Interactions;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Shared._KS14.GunDodger;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     The operators behind the Dodgenn's phases: drawing and throwing away its pistol, and dropping its dodge chance
///         when phase one ends.
/// </summary>
public sealed class KsDodgennPhaseTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly EntProtoId DodgennBoss = "KsMobDodgennBoss";
    private static readonly EntProtoId DodgennBossPhaseTwo = "KsMobDodgennBossPhaseTwo";
    private static readonly EntProtoId Pistol = "WeaponPistolMk58";

    /// <summary>
    ///     The boss starts out dodging everything, draws a pistol into its hand, deletes it again, and dodges less
    ///         once the transition sets its chance.
    /// </summary>
    [Test]
    public async Task TestPhaseOperators()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.EntMan;
        var testMap = await pair.CreateTestMap();

        EntityUid bossUid = default;
        EntityUid pistolUid = default;

        await server.WaitPost(() =>
        {
            bossUid = entManager.SpawnEntity(DodgennBoss, testMap.GridCoords);
            entManager.GetComponent<HTNComponent>(bossUid).Enabled = false;
        });

        await server.WaitAssertion(() =>
        {
            var handsSystem = entManager.System<SharedHandsSystem>();
            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, bossUid);

            Assert.That(entManager.GetComponent<GunDodgerComponent>(bossUid).DodgeChance, Is.EqualTo(1f),
                "phase one should dodge every shot");

            var spawnOperator = new SpawnInHandOperator { Proto = Pistol };
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(spawnOperator, oneOff: true);
            Assert.That(spawnOperator.Update(blackboard, 0f), Is.EqualTo(HTNOperatorStatus.Finished));

            Assert.That(handsSystem.TryGetActiveItem(bossUid, out var heldUid), "the pistol should be in the active hand");
            pistolUid = heldUid!.Value;
            Assert.That(entManager.GetComponent<MetaDataComponent>(pistolUid).EntityPrototype?.ID, Is.EqualTo(Pistol.Id));

            var deleteOperator = new DeleteActiveHandItemOperator();
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(deleteOperator, oneOff: true);
            Assert.That(deleteOperator.Update(blackboard, 0f), Is.EqualTo(HTNOperatorStatus.Finished));

            var dodgeOperator = new SetDodgeChanceOperator { DodgeChance = 0.4f };
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(dodgeOperator, oneOff: true);
            Assert.That(dodgeOperator.Update(blackboard, 0f), Is.EqualTo(HTNOperatorStatus.Finished));

            Assert.That(entManager.GetComponent<GunDodgerComponent>(bossUid).DodgeChance, Is.EqualTo(0.4f).Within(0.0001f),
                "phase two should dodge less");
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.Deleted(pistolUid), "the pistol should be deleted, not dropped");
        });
    }

    /// <summary>
    ///     The phase two variant spawns with the phase two marker on its blackboard (NpcInitialVirtualMarkers), and the
    ///         ordinary boss without it.
    /// </summary>
    [Test]
    public async Task TestPhaseTwoVariantStartsInPhaseTwo()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.EntMan;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var phaseTwoUid = entManager.SpawnEntity(DodgennBossPhaseTwo, testMap.GridCoords);
            var phaseOneUid = entManager.SpawnEntity(DodgennBoss, testMap.GridCoords);

            Assert.Multiple(() =>
            {
                Assert.That(HasVirtualMarkerPrecondition.HasMarker(entManager.GetComponent<HTNComponent>(phaseTwoUid).Blackboard, "DodgennPhaseTwo", entManager),
                    "the phase two variant should start with the marker");
                Assert.That(HasVirtualMarkerPrecondition.HasMarker(entManager.GetComponent<HTNComponent>(phaseOneUid).Blackboard, "DodgennPhaseTwo", entManager), Is.False,
                    "the ordinary boss should not");
            });
        });
    }
}
