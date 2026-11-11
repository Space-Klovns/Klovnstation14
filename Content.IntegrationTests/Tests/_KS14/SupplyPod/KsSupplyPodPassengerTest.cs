#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.SupplyPod.Passengers;
using Content.Shared.NPC;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.SupplyPod;

/// <summary>
///     A pod sits at its landing zone on the server for the whole fall, so an NPC riding one must not think until the pod
///         lets it out, or it opens fire on whoever stands around the landing zone from inside the pod.
/// </summary>
[TestFixture]
[TestOf(typeof(KsSupplyPodPassengerSystem))]
public sealed class KsSupplyPodPassengerTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private static readonly EntProtoId CargoPod = "SupplyPodCargoBaseSyndicate";

    private readonly EntProtoId _passenger = "KsTestPodPassenger";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestPodPassenger
  components:
  - type: MobState
  - type: HTN
    rootTask:
      task: IdleCompound
";

    [Test]
    public async Task PassengerSleepsUntilThePodOpens()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var containerSystem = server.System<SharedContainerSystem>();

        EntityUid podUid = default;
        EntityUid passengerUid = default;

        await server.WaitPost(() =>
        {
            podUid = server.EntMan.SpawnAtPosition(CargoPod, testMap.GridCoords);
            passengerUid = server.EntMan.SpawnAtPosition(_passenger, testMap.GridCoords);
            Assert.That(server.EntMan.HasComponent<ActiveNPCComponent>(passengerUid), Is.True,
                "a freshly spawned NPC should be awake, or the test proves nothing about boarding");

            Assert.That(server.System<KsSupplyPodPassengerSystem>().TryBoard(passengerUid, podUid), Is.True);
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(containerSystem.IsEntityInContainer(passengerUid), Is.True, "the pod should not have opened yet");
            Assert.That(server.EntMan.HasComponent<ActiveNPCComponent>(passengerUid), Is.False,
                "an NPC inside a falling pod must not think");

            // Healing back up wakes an NPC; inside a pod it must not.
            var mobStateSystem = server.System<MobStateSystem>();
            mobStateSystem.ChangeMobState(passengerUid, MobState.Critical);
            mobStateSystem.ChangeMobState(passengerUid, MobState.Alive);
            Assert.That(server.EntMan.HasComponent<ActiveNPCComponent>(passengerUid), Is.False,
                "coming back from critical inside the pod must not wake it");
        });

        // The cargo pod falls, then opens itself within a couple of seconds of landing.
        var tickRate = (float)server.ResolveDependency<IGameTiming>().TickRate;
        await Pair.RunTicksSync((int)(8f * tickRate));

        await server.WaitAssertion(() =>
        {
            Assert.That(containerSystem.IsEntityInContainer(passengerUid), Is.False, "the pod should have opened by now");
            Assert.That(server.EntMan.HasComponent<ActiveNPCComponent>(passengerUid), Is.True,
                "an NPC let out of its pod should be awake");
            Assert.That(server.EntMan.HasComponent<KsSupplyPodPassengerComponent>(passengerUid), Is.False);
        });
    }
}
