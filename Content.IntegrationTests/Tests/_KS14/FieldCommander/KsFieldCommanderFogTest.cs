#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Shared._KS14.FieldCommander;
using Content.Shared.Damage.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     The fog hurts defenders who are outside the play area, and nobody else.
/// </summary>
[TestFixture]
public sealed class KsFieldCommanderFogTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _defender = "KsTestFieldCommanderFogDefender";
    private readonly EntProtoId _bystander = "KsTestFieldCommanderFogBystander";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFieldCommanderFogBystander
  components:
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      200: Dead
  - type: Damageable
    damageContainer: Biological
  - type: Injurable

- type: entity
  parent: KsTestFieldCommanderFogBystander
  id: KsTestFieldCommanderFogDefender
  components:
  - type: ScenarioFactionMember
    id: Nanotrasen
";

    [Test]
    public async Task OnlyDefendersOutsideTheRangeAreHurt()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();
        EntityUid inside = default, outside = default, bystander = default;

        await server.WaitPost(() =>
        {
            server.System<KsFieldCommanderFogSystem>().SetFog(map.MapUid, Vector2.Zero, 10f);
            inside = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(map.MapUid, 3f, 0f));
            outside = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(map.MapUid, 14f, 0f));
            bystander = server.EntMan.SpawnAtPosition(_bystander, new EntityCoordinates(map.MapUid, 14f, 0f));
        });

        await Pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That((float)server.System<DamageableSystem>().GetTotalDamage(outside), Is.GreaterThan(0f),
                "a defender in the fog should be hurt");
            Assert.That((float)server.System<DamageableSystem>().GetTotalDamage(inside), Is.EqualTo(0f),
                "a defender inside the range should be fine");
            Assert.That((float)server.System<DamageableSystem>().GetTotalDamage(bystander), Is.EqualTo(0f),
                "someone who is not a defender should be fine");
        });

        await Pair.Client.WaitAssertion(() =>
        {
            var clientMap = Pair.ToClientUid(map.MapUid);
            Assert.That(Pair.Client.EntMan.GetComponent<KsFieldCommanderFogComponent>(clientMap).Range, Is.EqualTo(10f));
        });
    }
}
