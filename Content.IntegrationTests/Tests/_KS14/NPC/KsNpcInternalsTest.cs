#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Internals;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     An NPC putting its internals on and taking them off by what it is about to breathe (<see cref="NpcInternalsSystem"/>).
/// </summary>
/// <remarks>
///     Breaths are taken by calling <see cref="RespiratorSystem.Inhale"/>, and smoke is breathed by calling
///         <see cref="SmokeSystem.SmokeReact"/>, so nothing waits on a respiration cycle or a collision.
/// </remarks>
[TestFixture]
[TestOf(typeof(NpcInternalsSystem))]
public sealed class KsNpcInternalsTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Mob = "KsNpcInternalsTestMob";
    private const string MobWithout = "KsNpcInternalsTestMobWithout";
    private const string Reagent = "TearGas";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsNpcInternalsTestMob
  components:
  - type: Hands
  - type: Inventory
  - type: ContainerContainer
  - type: Internals
  - type: SolutionContainerManager
  - type: Respirator
    damage:
      types:
        Asphyxiation: 1
    damageRecovery:
      types:
        Asphyxiation: -1
  - type: Bloodstream
    bloodlossDamage:
      types:
        Bloodloss: 1
    bloodlossHealDamage:
      types:
        Bloodloss: -1
  - type: NpcInternalsUser
    holdTime: 0.5

- type: entity
  parent: KsNpcInternalsTestMob
  id: KsNpcInternalsTestMobWithout
  components:
  - type: NpcInternalsUser
    holdTime: 0

- type: entity
  id: KsNpcInternalsTestMask
  components:
  - type: Item
  - type: BreathMask

- type: entity
  parent: KsNpcTestOxygenTank
  id: KsNpcInternalsTestTank

- type: entity
  parent: KsNpcTestSmoke
  id: KsNpcInternalsTestSmoke
";

    /// <summary>
    ///     A breath of vacuum puts them on, and that same breath already comes from the tank.
    /// </summary>
    [Test]
    public async Task TestBadAirPutsThemOnInTime()
    {
        var (mobUid, tankUid, _) = await SetUp(breathable: false);

        await Server.WaitAssertion(() =>
        {
            var tankAir = SEntMan.GetComponent<GasTankComponent>(tankUid).Air;
            var molesBefore = tankAir.TotalMoles;

            Assert.That(Server.System<InternalsSystem>().AreInternalsWorking(mobUid), Is.False);

            Server.System<RespiratorSystem>().Inhale(mobUid);

            Assert.That(Server.System<InternalsSystem>().AreInternalsWorking(mobUid), "a breath of vacuum should put them on");
            Assert.That(tankAir.TotalMoles, Is.LessThan(molesBefore), "and that breath should come from the tank");
        });
    }

    /// <summary>
    ///     Breathable air leaves them off, and takes them off once the last bad breath is <c>holdTime</c> behind it, and
    ///         not before.
    /// </summary>
    [Test]
    public async Task TestGoodAirTakesThemOffInTime()
    {
        var (mobUid, _, mapUid) = await SetUp(breathable: false);
        var internalsSystem = Server.System<InternalsSystem>();
        var respiratorSystem = Server.System<RespiratorSystem>();

        await Server.WaitAssertion(() =>
        {
            respiratorSystem.Inhale(mobUid);
            Assert.That(internalsSystem.AreInternalsWorking(mobUid));

            MakeBreathable(mapUid);
            respiratorSystem.Inhale(mobUid);
            Assert.That(internalsSystem.AreInternalsWorking(mobUid), "still within holdTime of the bad breath");
        });

        // Past holdTime, and the tank's own use delay.
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            respiratorSystem.Inhale(mobUid);
            Assert.That(internalsSystem.AreInternalsWorking(mobUid), Is.False, "a safe breath long after should take them off");

            respiratorSystem.Inhale(mobUid);
            Assert.That(internalsSystem.AreInternalsWorking(mobUid), Is.False, "and safe air should leave them off");
        });
    }

    /// <summary>
    ///     Internals somebody else put on stay on, whatever the air.
    /// </summary>
    [Test]
    public async Task TestOnlyTakesOffItsOwn()
    {
        var (mobUid, tankUid, _) = await SetUp(breathable: true);
        var internalsSystem = Server.System<InternalsSystem>();

        await Server.WaitAssertion(() =>
        {
            var tank = new Entity<GasTankComponent>(tankUid, SEntMan.GetComponent<GasTankComponent>(tankUid));
            Assert.That(Server.System<GasTankSystem>().ConnectToInternals(tank));
        });

        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            Server.System<RespiratorSystem>().Inhale(mobUid);
            Assert.That(internalsSystem.AreInternalsWorking(mobUid), "it should leave on what it did not put on");
        });
    }

    /// <summary>
    ///     Told to keep them on, it does, in good air as well.
    /// </summary>
    [Test]
    public async Task TestKeepOnInGoodAir()
    {
        var (mobUid, _, _) = await SetUp(breathable: true);
        var internalsSystem = Server.System<InternalsSystem>();

        await Server.WaitAssertion(() =>
        {
            var npcInternalsSystem = Server.System<NpcInternalsSystem>();
            Assert.That(npcInternalsSystem.CanGoOnInternals(mobUid));
            Assert.That(npcInternalsSystem.KeepOn(mobUid, Server.Timing.CurTime + TimeSpan.FromSeconds(30)));

            Server.System<RespiratorSystem>().Inhale(mobUid);
            Assert.That(internalsSystem.AreInternalsWorking(mobUid), "good air should not take off what it was told to keep on");
        });
    }

    /// <summary>
    ///     Smoke carrying something puts them on before any of it gets in. One that does not opt in gets a lungful, which
    ///         shows the smoke would have got in.
    /// </summary>
    [TestCase(Mob, false)]
    [TestCase(MobWithout, true)]
    public async Task TestSmokeIsKeptOut(string mob, bool breathedIn)
    {
        var (mobUid, _, _) = await SetUp(breathable: true, mob: mob);

        await Server.WaitAssertion(() =>
        {
            if (mob == MobWithout)
                SEntMan.RemoveComponent<NpcInternalsUserComponent>(mobUid);

            var smokeUid = SEntMan.SpawnEntity("KsNpcInternalsTestSmoke", SEntMan.GetComponent<TransformComponent>(mobUid).Coordinates);
            var smokeSystem = Server.System<SmokeSystem>();
            smokeSystem.StartSmoke(smokeUid, new Solution(Reagent, 20), 10f, 0);
            smokeSystem.SmokeReact(mobUid, smokeUid);

            Assert.That(Server.System<InternalsSystem>().AreInternalsWorking(mobUid), Is.Not.EqualTo(breathedIn));
            Assert.That(GetInBlood(mobUid) > 0, Is.EqualTo(breathedIn), $"{Reagent} in the blood");
        });
    }

    /// <summary>
    ///     Smoke that carries nothing is no reason to put them on.
    /// </summary>
    [Test]
    public async Task TestPlainSmokeLeavesThemOff()
    {
        var (mobUid, _, _) = await SetUp(breathable: true);

        await Server.WaitAssertion(() =>
        {
            var smokeUid = SEntMan.SpawnEntity("KsNpcInternalsTestSmoke", SEntMan.GetComponent<TransformComponent>(mobUid).Coordinates);
            var smokeSystem = Server.System<SmokeSystem>();
            smokeSystem.StartSmoke(smokeUid, new Solution(), 10f, 0);
            smokeSystem.SmokeReact(mobUid, smokeUid);

            Assert.That(Server.System<InternalsSystem>().AreInternalsWorking(mobUid), Is.False);
        });
    }

    private float GetInBlood(EntityUid mobUid)
    {
        var bloodstreamComponent = SEntMan.GetComponent<BloodstreamComponent>(mobUid);
        var solutionContainerSystem = Server.System<SharedSolutionContainerSystem>();

        return solutionContainerSystem.ResolveSolution(mobUid, bloodstreamComponent.BloodSolutionName, ref bloodstreamComponent.BloodSolution, out var solution)
            ? solution.GetTotalPrototypeQuantity(Reagent).Float()
            : 0f;
    }

    /// <summary>
    ///     A mob with a mask connected and a tank in hand, on a map of vacuum or of breathable air.
    /// </summary>
    private async Task<(EntityUid Mob, EntityUid Tank, EntityUid Map)> SetUp(bool breathable, string mob = Mob)
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid mobUid = default, tankUid = default;

        await Server.WaitPost(() =>
        {
            if (breathable)
                MakeBreathable(testMap.MapUid);

            mobUid = SEntMan.SpawnEntity(mob, testMap.GridCoords);

            var maskUid = SEntMan.SpawnEntity("KsNpcInternalsTestMask", testMap.GridCoords);
            var internalsComponent = SEntMan.GetComponent<InternalsComponent>(mobUid);
            Server.System<InternalsSystem>().ConnectBreathTool((mobUid, internalsComponent), maskUid);

            var handsSystem = Server.System<SharedHandsSystem>();
            handsSystem.AddHand(mobUid, "hand", HandLocation.Right);
            tankUid = SEntMan.SpawnEntity("KsNpcInternalsTestTank", testMap.GridCoords);
            Assert.That(handsSystem.TryPickup(mobUid, tankUid, "hand"), "the test needs it holding a tank");
        });

        await Pair.RunTicksSync(5);
        return (mobUid, tankUid, testMap.MapUid);
    }

    private void MakeBreathable(EntityUid mapUid)
    {
        var moles = new float[Atmospherics.AdjustedNumberOfGases];
        moles[(int) Gas.Oxygen] = 21.824779f;
        moles[(int) Gas.Nitrogen] = 82.10312f;
        Server.System<AtmosphereSystem>().SetMapAtmosphere(mapUid, space: false, new GasMixture(moles, Atmospherics.T20C));
    }
}
