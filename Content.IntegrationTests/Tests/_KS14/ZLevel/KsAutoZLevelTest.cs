#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.ZLevel.Auto;
using Content.Shared._KS14.ZLevel;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.ZLevel;

/// <summary>
///     Auto z-levels: two maps saved paused with the same id link into one stack when one of them is unpaused.
/// </summary>
[TestFixture]
[TestOf(typeof(KsAutoZLevelSystem))]
public sealed class KsAutoZLevelTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private readonly EntProtoId _lowerAuto = "KsTestAutoZLevelLower";
    private readonly EntProtoId _upperAuto = "KsTestAutoZLevelUpper";

    // Carriers for the components, added to the maps: KsAutoZLevelComponent's fields are the system's to write.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestAutoZLevelLower
  components:
  - type: KsAutoZLevel
    id: ks-test-auto-z-level
    location: Under

- type: entity
  id: KsTestAutoZLevelUpper
  components:
  - type: KsAutoZLevel
    id: ks-test-auto-z-level
    location: Above
";

    /// <summary>
    ///     Unpausing the first map unpauses its partner from inside the link, and the partner's own unpause must not
    ///         link the pair a second time (which logged "already part of" and failed the test on the error log).
    /// </summary>
    [Test]
    public async Task UnpausingOneOfTwoPausedPartnersLinksThemOnce()
    {
        EntityUid lower = default;
        EntityUid upper = default;

        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            lower = mapSystem.CreateMap(out _); // initialized, then paused below: how auto z-level maps are loaded
            upper = mapSystem.CreateMap(out _); // initialized, then paused below: how auto z-level maps are loaded
            mapSystem.SetPaused(lower, true);
            mapSystem.SetPaused(upper, true);

            SEntMan.AddComponents(lower, SProtoMan.Index(_lowerAuto));
            SEntMan.AddComponents(upper, SProtoMan.Index(_upperAuto));
        });

        await Server.WaitPost(() => SEntMan.System<SharedMapSystem>().SetPaused(upper, false));
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            Assert.That(mapSystem.IsPaused(lower), Is.False, "the partner was unpaused too");

            var upperZLevel = SEntMan.GetComponent<KsZLevelComponent>(upper);
            var lowerZLevel = SEntMan.GetComponent<KsZLevelComponent>(lower);
            Assert.That(upperZLevel.AssociatedStack, Is.SameAs(lowerZLevel.AssociatedStack));
            Assert.That(upperZLevel.AssociatedStack, Has.Count.EqualTo(2), "linked once");
            Assert.That(upperZLevel.Node?.Previous?.Value.Owner, Is.EqualTo(lower), "the one set above is above");

            Assert.That(SEntMan.HasComponent<KsAutoZLevelComponent>(upper) || SEntMan.HasComponent<KsAutoZLevelComponent>(lower),
                Is.False, "both are done linking");
        });

        await Server.WaitPost(() =>
        {
            SEntMan.DeleteEntity(upper);
            SEntMan.DeleteEntity(lower);
        });
    }
}
