using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Components;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared.Mind;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     The ways of cultivating faster actually work, for qi and body cultivators alike.
/// </summary>
[TestFixture]
[TestOf(typeof(JianghuSystem))]
public sealed class CultivationSpeedTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: KsTestSpeedBody
  components:
  - type: BodyCultivator
    committed: true
- type: entity
  parent: MobHuman
  id: KsTestSpeedQi
  components:
  - type: Cultivator
";

    private void GiveMind(EntityUid uid)
    {
        var mindSystem = SEntMan.System<SharedMindSystem>();
        mindSystem.TransferTo(mindSystem.CreateMind(null).Owner, uid);
    }

    private float Train(EntityUid uid)
    {
        return SEntMan.System<SharedBodyCultivationSystem>().AddTempering(uid, 10f, trainingSource: "test");
    }

    private float InsightMultiplier(EntityUid uid)
    {
        var ev = new CultivationInsightMultiplierEvent(1f);
        SEntMan.EventBus.RaiseLocalEvent(uid, ref ev);
        return ev.Multiplier;
    }

    [Test]
    public async Task TestSwornSiblingsCultivateFaster()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var aloneBody = SEntMan.SpawnEntity("KsTestSpeedBody", map.GridCoords);
            var swornBody = SEntMan.SpawnEntity("KsTestSpeedBody", map.GridCoords);
            var aloneQi = SEntMan.SpawnEntity("KsTestSpeedQi", map.GridCoords);
            var swornQi = SEntMan.SpawnEntity("KsTestSpeedQi", map.GridCoords);
            SEntMan.EnsureComponent<SwornTogetherComponent>(swornBody);
            SEntMan.EnsureComponent<SwornTogetherComponent>(swornQi);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Train(swornBody), Is.GreaterThan(Train(aloneBody)), "A sworn sibling at their side didn't speed body training.");
                Assert.That(InsightMultiplier(swornQi), Is.GreaterThan(InsightMultiplier(aloneQi)), "A sworn sibling at their side didn't speed meditation.");
            }
        });
    }

    [Test]
    public async Task TestSectFellowsCultivateFaster()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var jianghuSystem = SEntMan.System<JianghuSystem>();
            var masterUid = SEntMan.SpawnEntity("KsTestSpeedQi", map.GridCoords);
            var discipleUid = SEntMan.SpawnEntity("KsTestSpeedBody", map.GridCoords.Offset(new Vector2(1f, 0f)));
            var loneUid = SEntMan.SpawnEntity("KsTestSpeedBody", map.GridCoords);
            foreach (var uid in new[] { masterUid, discipleUid, loneUid })
                GiveMind(uid);

            var loneMultiplier = InsightMultiplier(masterUid);
            var loneTraining = Train(loneUid);
            var sect = jianghuSystem.FoundSect(masterUid, "Speedy Lotus Sect");
            Assert.That(sect, Is.Not.Null, "The sect wasn't founded.");
            jianghuSystem.AddMember(sect.Value, discipleUid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(InsightMultiplier(masterUid), Is.GreaterThan(loneMultiplier), "Fellow members nearby didn't speed meditation.");
                Assert.That(Train(discipleUid), Is.GreaterThan(loneTraining), "Fellow members nearby didn't speed body training.");
            }
        });
    }

    [Test]
    public async Task TestGatheringArrayFeedsQi()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var cultivationSystem = SEntMan.System<CultivationSystem>();
            var uid = SEntMan.SpawnEntity("KsTestSpeedQi", map.GridCoords);
            var before = cultivationSystem.QiSourceMultiplier(uid);
            SEntMan.SpawnEntity("KsGatheringArray", map.GridCoords);
            Assert.That(cultivationSystem.QiSourceMultiplier(uid), Is.GreaterThan(before), "Sitting in a gathering array didn't help.");
        });
    }

    [Test]
    public async Task TestTrainingRespectsItsCooldown()
    {
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity("KsTestSpeedBody", Robust.Shared.Map.MapCoordinates.Nullspace);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Train(uid), Is.GreaterThan(0f), "Training earned nothing.");
                Assert.That(Train(uid), Is.Zero, "The same training paid out twice at once.");
            }
        });
    }
}
