using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Damage.DamagedParticles;
using Content.Shared._KS14.Particles;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Damage;

/// <summary>
///     <see cref="KsDamagedParticlesComponent"/>: the emitter is off below the threshold, on from it, and off again
///         once the damage is repaired.
/// </summary>
[TestFixture]
[TestOf(typeof(KsDamagedParticlesSystem))]
public sealed class KsDamagedParticlesTest : GameTest
{
    private const int Threshold = 10;
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";
    private static readonly EntProtoId Mower = "KsLawnmower";

    // An id that exists only in the test prototypes below.
    private readonly EntProtoId _machine = "KsDamagedParticlesTestMachine";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsDamagedParticlesTestMachine
  components:
  - type: Damageable
  - type: Injurable
  - type: KsParticles
    prototype: KsSmoke
    emitting: false
  - type: KsDamagedParticles
    threshold: 10
";

    [Test]
    public async Task SmokesFromTheThresholdUntilRepaired()
    {
        var testMap = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var machineUid = SSpawnAtPosition(_machine, testMap.GridCoords);
            var damageableSystem = Server.System<DamageableSystem>();
            var particlesComponent = SComp<KsParticlesComponent>(machineUid);
            var blunt = Server.ProtoMan.Index(Blunt);

            Assert.That(particlesComponent.Emitting, Is.False, "An undamaged machine should not smoke.");

            damageableSystem.TryChangeDamage(machineUid, new DamageSpecifier(blunt, Threshold - 1), ignoreResistances: true);
            Assert.That(particlesComponent.Emitting, Is.False, "Damage below the threshold should not start the smoke.");

            damageableSystem.TryChangeDamage(machineUid, new DamageSpecifier(blunt, 1), ignoreResistances: true);
            Assert.That(particlesComponent.Emitting, Is.True, "Damage at the threshold should start the smoke.");

            damageableSystem.TryChangeDamage(machineUid, new DamageSpecifier(blunt, -1), ignoreResistances: true);
            Assert.That(particlesComponent.Emitting, Is.False, "Repairing below the threshold should stop the smoke.");
        });
    }

    [Test]
    public async Task DamagedLawnmowerSmokesUntilRepaired()
    {
        var testMap = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var mowerUid = SSpawnAtPosition(Mower, testMap.GridCoords);
            var damageableSystem = Server.System<DamageableSystem>();
            var particlesComponent = SComp<KsParticlesComponent>(mowerUid);
            var threshold = SComp<KsDamagedParticlesComponent>(mowerUid).Threshold;

            Assert.That(particlesComponent.Emitting, Is.False, "A new mower should not smoke.");

            damageableSystem.TryChangeDamage(mowerUid, new DamageSpecifier(Server.ProtoMan.Index(Blunt), threshold), ignoreResistances: true);
            Assert.That(Server.EntMan.EntityExists(mowerUid), Is.True, "The smoke threshold should be below the mower's destruction.");
            Assert.That(particlesComponent.Emitting, Is.True, "A damaged mower should smoke.");

            damageableSystem.ClearAllDamage(mowerUid);
            Assert.That(particlesComponent.Emitting, Is.False, "A repaired mower should stop smoking.");
        });
    }
}
