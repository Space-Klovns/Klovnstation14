#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Shared._KS14.FieldCommander;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.Damage.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     The fog hurts members of the faction it was raised for who are outside the play area, more the further out they
///         are, and nobody else.
/// </summary>
[TestFixture]
public sealed class KsFieldCommanderFogTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly ProtoId<ScenarioFactionPrototype> Nanotrasen = "Nanotrasen";
    private static readonly ProtoId<ScenarioFactionPrototype> Syndicate = "Syndicate";

    private const float Range = 10f;

    // Exists only in the test prototypes below.
    private readonly EntProtoId _mob = "KsTestFieldCommanderFogMob";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFieldCommanderFogMob
  components:
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      1000: Dead
  - type: Damageable
    damageContainer: Biological
  - type: Injurable
";

    [Test]
    public async Task OnlyTheFogsFactionOutsideTheRangeIsHurt([Values] bool forNanotrasen)
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();
        var faction = forNanotrasen ? Nanotrasen : Syndicate;
        var otherFaction = forNanotrasen ? Syndicate : Nanotrasen;
        EntityUid inside = default, nearOutside = default, farOutside = default, otherSide = default, noSide = default;

        await server.WaitPost(() =>
        {
            var fogSystem = server.System<KsFieldCommanderFogSystem>();
            fogSystem.SetFog(map.MapUid, Vector2.Zero, Range, faction);
            var fogComponent = server.EntMan.GetComponent<KsFieldCommanderFogComponent>(map.MapUid);

            EntityUid Spawn(float x, ProtoId<ScenarioFactionPrototype>? side)
            {
                var uid = server.EntMan.SpawnAtPosition(_mob, new EntityCoordinates(map.MapUid, x, 0f));
                if (side is { } sideId)
                    server.EntMan.AddComponent<ScenarioFactionMemberComponent>(uid).Id = sideId;
                return uid;
            }

            // Far out enough for the damage to have doubled twice, still under its cap.
            var nearX = Range + 1f;
            var farX = nearX + 2f * fogComponent.DoublingDistance;
            Assert.That(MathF.Pow(2f, (farX - Range) / fogComponent.DoublingDistance), Is.LessThan(fogComponent.MaxDamageScale),
                "the far defender must be short of the damage cap, or it can't take more than the near one");

            inside = Spawn(Range - fogComponent.WarningMargin - 1f, faction);
            nearOutside = Spawn(nearX, faction);
            farOutside = Spawn(farX, faction);
            otherSide = Spawn(farX, otherFaction);
            noSide = Spawn(farX, side: null);
        });

        await RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            var damageableSystem = server.System<DamageableSystem>();
            float Damage(EntityUid uid) => (float)damageableSystem.GetTotalDamage(uid);

            Assert.That(Damage(nearOutside), Is.GreaterThan(0f), "a defender in the fog should be hurt");
            Assert.That(Damage(farOutside), Is.GreaterThan(Damage(nearOutside)), "the further out, the worse it gets");
            Assert.That(Damage(inside), Is.EqualTo(0f), "a defender inside the range should be fine");
            Assert.That(Damage(otherSide), Is.EqualTo(0f), "the other side isn't hurt by the defenders' fog");
            Assert.That(Damage(noSide), Is.EqualTo(0f), "someone on no side should be fine");
        });

        await Pair.Client.WaitAssertion(() =>
        {
            var clientMap = Pair.ToClientUid(map.MapUid);
            Assert.That(Pair.Client.EntMan.GetComponent<KsFieldCommanderFogComponent>(clientMap).Range, Is.EqualTo(Range),
                "the client draws the fog, so it needs the range");
        });
    }
}
