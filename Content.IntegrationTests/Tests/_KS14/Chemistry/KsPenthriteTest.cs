#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Administration.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Chemistry;

/// <summary>
///     tg's penthrite: it holds a body up past where it would go critical, heals it while it is that hurt, and goes off
///         with epinephrine.
/// </summary>
[TestFixture]
public sealed class KsPenthriteTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId Beaker = "Beaker";
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";
    private const string Penthrite = "Penthrite";
    private const string Epinephrine = "Epinephrine";

    private int TicksFor(float seconds) => (int)(seconds * (float)Server.ResolveDependency<IGameTiming>().TickRate);

    [Test]
    public async Task PenthriteHoldsABodyUpPastCritAndHealsIt()
    {
        var testMap = await Pair.CreateTestMap();
        var coordinates = new EntityCoordinates(testMap.Grid, 0.5f, 0.5f);
        var humanUid = EntityUid.Invalid;
        var damageAfterHit = FixedPoint2.Zero;

        await Server.WaitAssertion(() =>
        {
            // Air, so the vacuum's barotrauma and cold don't hurt the body while it heals.
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            mixture.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            mixture.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, false, mixture);

            humanUid = SEntMan.SpawnEntity(Human, coordinates);
            var solutionContainerSystem = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutionContainerSystem.TryGetInjectableSolution(humanUid, out var solution, out _), Is.True);
            Assert.That(solutionContainerSystem.TryAddReagent(solution!.Value, Penthrite, 10), Is.True);
        });
        await Pair.RunTicksSync(TicksFor(2f));

        await Server.WaitAssertion(() =>
        {
            var damage = new DamageSpecifier(SProtoMan.Index(Blunt), 150);
            SEntMan.System<DamageableSystem>().TryChangeDamage(humanUid, damage, ignoreResistances: true);
            damageAfterHit = SEntMan.System<DamageableSystem>().GetTotalDamage(humanUid);
            Assert.That(damageAfterHit, Is.GreaterThan(FixedPoint2.New(100)), "precondition: the hit is past where a human goes critical");
        });
        await Pair.RunTicksSync(TicksFor(3f));

        await Server.WaitAssertion(() =>
        {
            var mobStateSystem = SEntMan.System<MobStateSystem>();
            Assert.That(mobStateSystem.IsAlive(humanUid), Is.True, "Penthrite must hold the body up past crit.");
            // A body heals a little by itself; penthrite heals about two a second.
            Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(humanUid), Is.LessThan(damageAfterHit - 3),
                "Penthrite must heal a body hurt that badly.");

            // Whole again, so the body doesn't go critical as the map is deleted under it.
            SEntMan.System<RejuvenateSystem>().PerformRejuvenate(humanUid);
        });
    }

    [Test]
    public async Task PenthriteGoesOffWithEpinephrine()
    {
        var testMap = await Pair.CreateTestMap();
        var coordinates = new EntityCoordinates(testMap.Grid, 0.5f, 0.5f);

        await Server.WaitAssertion(() =>
        {
            var beakerUid = SEntMan.SpawnEntity(Beaker, coordinates);
            var solutionContainerSystem = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutionContainerSystem.TryGetRefillableSolution(beakerUid, out var solution, out _), Is.True);
            Assert.That(solutionContainerSystem.TryAddReagent(solution!.Value, Penthrite, 5), Is.True);
            Assert.That(solutionContainerSystem.TryAddReagent(solution.Value, Epinephrine, 5), Is.True);

            var left = solution.Value.Comp.Solution;
            Assert.That(left.GetTotalPrototypeQuantity(Penthrite), Is.EqualTo(FixedPoint2.Zero), "Penthrite must react with epinephrine.");
            Assert.That(left.GetTotalPrototypeQuantity(Epinephrine), Is.EqualTo(FixedPoint2.Zero), "Epinephrine must react with penthrite.");
        });
    }
}
