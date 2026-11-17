#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared.Damage.Systems;
using Content.Shared.Flash;
using Content.Shared.StatusEffectNew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Flash;

/// <summary>
///     <see cref="SharedFlashSystem.CanFlash"/> says what a flash could reach, and <see cref="SharedFlashSystem.Flash"/> goes
///         by it. Only a real flash does anything.
/// </summary>
public sealed class KsCanFlashTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Shade = "KsCanFlashTestShade";
    private const string ProtectedShade = "KsCanFlashTestProtectedShade";
    private const string Blindness = "KsCanFlashTestBlindness";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsCanFlashTestShade
  components:
  - type: Damageable
  - type: Injurable
  - type: DamagedByFlashing
    flashDamage:
      types:
        Heat: 10

- type: entity
  parent: KsCanFlashTestShade
  id: KsCanFlashTestProtectedShade
  components:
  - type: FlashImmunity

- type: entity
  id: KsCanFlashTestBlindness
  components:
  - type: StatusEffect
  - type: BlindnessStatusEffect
";

    /// <summary>
    ///     A flash hurts what flashes hurt, unless it could not reach it.
    /// </summary>
    [TestCase(Shade, true)]
    [TestCase(ProtectedShade, false)]
    public async Task TestFlashHurtsWhatItReaches(string prototype, bool hurts)
    {
        var entManager = Pair.Server.EntMan;

        await Pair.Server.WaitAssertion(() =>
        {
            var uid = entManager.SpawnEntity(prototype, MapCoordinates.Nullspace);

            entManager.System<SharedFlashSystem>().Flash(uid, null, null, TimeSpan.FromSeconds(1), 1f, displayPopup: false);

            var damage = entManager.System<DamageableSystem>().GetTotalDamage(uid).Float();
            Assert.That(damage > 0f, Is.EqualTo(hurts), $"took {damage} damage");
        });
    }

    /// <summary>
    ///     Asking can happen any number of times, for a flash that never comes, so it does nothing.
    /// </summary>
    [Test]
    public async Task TestAskingChangesNothing()
    {
        var entManager = Pair.Server.EntMan;

        await Pair.Server.WaitAssertion(() =>
        {
            var uid = entManager.SpawnEntity(Shade, MapCoordinates.Nullspace);

            Assert.Multiple(() =>
            {
                Assert.That(entManager.System<SharedFlashSystem>().CanFlash(uid), "nothing protects it");
                Assert.That(entManager.System<DamageableSystem>().GetTotalDamage(uid).Float(), Is.Zero, "and asking should not have hurt it");
            });
        });
    }

    /// <summary>
    ///     Status effects are asked too: what cannot see cannot be flashed.
    /// </summary>
    [Test]
    public async Task TestBlindnessStopsFlashes()
    {
        var entManager = Pair.Server.EntMan;

        await Pair.Server.WaitAssertion(() =>
        {
            var flashSystem = entManager.System<SharedFlashSystem>();
            var uid = entManager.SpawnEntity(Shade, MapCoordinates.Nullspace);

            Assert.That(flashSystem.CanFlash(uid), "it can see");
            Assert.That(entManager.System<StatusEffectsSystem>().TryAddStatusEffectDuration(uid, Blindness, TimeSpan.FromMinutes(1)), "blindness should apply");
            Assert.That(flashSystem.CanFlash(uid), Is.False, "now it cannot");
        });
    }

    /// <summary>
    ///     A flash that pierces protection reaches what is protected, and the answer has to say so.
    /// </summary>
    [Test]
    public async Task TestOverridePiercesProtection()
    {
        var entManager = Pair.Server.EntMan;

        await Pair.Server.WaitAssertion(() =>
        {
            var flashSystem = entManager.System<SharedFlashSystem>();
            var uid = entManager.SpawnEntity(ProtectedShade, MapCoordinates.Nullspace);

            Assert.Multiple(() =>
            {
                Assert.That(flashSystem.CanFlash(uid), Is.False, "protection stops an ordinary flash");
                Assert.That(flashSystem.CanFlash(uid, flashOverride: true), "but not this one");
            });
        });
    }
}
