#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Bane;
using Content.Shared.Damage.Systems;
using Content.Shared.Throwing;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Bane;

[TestFixture]
[TestOf(typeof(KsBaneSystem))]
public sealed class KsBaneTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private static readonly EntProtoId Spear = "KsSpearGiantslayer";
    private readonly EntProtoId _fauna = "KsTestBaneFauna";
    private readonly EntProtoId _bystander = "KsTestBaneBystander";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestBaneBystander
  components:
  - type: Damageable
  - type: Injurable
    damageContainer: Biological

- type: entity
  parent: KsTestBaneBystander
  id: KsTestBaneFauna
  components:
  - type: KsMiningFauna
";

    [Test]
    public async Task GiantslayerHitsMiningFaunaHarder()
    {
        await Server.WaitAssertion(() =>
        {
            var spearUid = SEntMan.SpawnEntity(Spear, MapCoordinates.Nullspace);
            var faunaUid = SEntMan.SpawnEntity(_fauna, MapCoordinates.Nullspace);
            var bystanderUid = SEntMan.SpawnEntity(_bystander, MapCoordinates.Nullspace);
            var thrownComponent = new ThrownItemComponent();

            var faunaHit = new ThrowDoHitEvent(spearUid, faunaUid, thrownComponent);
            SEntMan.EventBus.RaiseLocalEvent(spearUid, ref faunaHit);
            var bystanderHit = new ThrowDoHitEvent(spearUid, bystanderUid, thrownComponent);
            SEntMan.EventBus.RaiseLocalEvent(spearUid, ref bystanderHit);

            var damageableSystem = SEntMan.System<DamageableSystem>();
#pragma warning disable CS0618 // a total is exactly what the bane adds
            var faunaDamage = damageableSystem.GetTotalDamage(faunaUid);
            var bystanderDamage = damageableSystem.GetTotalDamage(bystanderUid);
#pragma warning restore CS0618

            // The spear's ordinary thrown damage lands on both; only the fauna takes the bane's 80 on top.
            Assert.That((float)(faunaDamage - bystanderDamage), Is.EqualTo(80f).Within(0.01f),
                "Mining fauna must take the bane's extra 80 damage.");
        });
    }
}
