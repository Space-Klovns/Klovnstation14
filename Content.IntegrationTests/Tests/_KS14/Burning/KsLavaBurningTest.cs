#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Gravity;
using Content.Shared._KS14.Burning;
using Content.Shared.Gravity;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Burning;

[TestFixture]
[TestOf(typeof(KsItemBurningSystem))]
public sealed class KsLavaBurningTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private static readonly EntProtoId Lava = "FloorLavaEntity";
    private static readonly EntProtoId Ash = "KsAsh";
    private static readonly EntProtoId UnfiredSpear = "KsSpearGiantslayerUnfired";
    private static readonly EntProtoId FiredSpear = "KsSpearGiantslayer";
    private readonly EntProtoId _plainItem = "KsTestLavaItem";
    private readonly EntProtoId _lavaProofItem = "KsTestLavaProofItem";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseItem
  id: KsTestLavaItem

- type: entity
  parent: BaseItem
  id: KsTestLavaProofItem
  components:
  - type: KsLavaProof
";

    private async Task<(EntityUid ItemUid, EntityCoordinates Coordinates)> DropInLavaAsync(EntProtoId item)
    {
        var testMap = await Pair.CreateTestMap();
        var coordinates = new EntityCoordinates(testMap.Grid, 0.5f, 0.5f);
        var itemUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            // Lava, like any step trigger, ignores weightless items; real lava sits on a planet with gravity.
            SEntMan.EnsureComponent<GravityComponent>(testMap.Grid);
            SEntMan.System<GravitySystem>().EnableGravity(testMap.Grid);
            SEntMan.SpawnEntity(Lava, coordinates);
            itemUid = SEntMan.SpawnEntity(item, coordinates);
        });
        await Pair.RunTicksSync(10);
        return (itemUid, coordinates);
    }

    private int TicksFor(float seconds) => (int)(seconds * (float)Server.ResolveDependency<IGameTiming>().TickRate);

    [Test]
    public async Task ItemsInLavaBurnSlowlyToAsh()
    {
        var (itemUid, _) = await DropInLavaAsync(_plainItem);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<KsItemBurningComponent>(itemUid), Is.True, "An item in lava must catch fire.");
            Assert.That(SEntMan.Deleted(itemUid), Is.False, "It must burn slowly, not vanish.");
        });

        // 200 integrity: 10 at once, then 5 a second.
        await Pair.RunTicksSync(TicksFor(45f));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(itemUid), Is.True, "The item must have burned away.");
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>().Any(meta => meta.EntityPrototype?.ID == Ash.Id), Is.True,
                "Burning away must leave ash.");
        });
    }

    [Test]
    public async Task LavaProofItemsDoNotBurn()
    {
        var (itemUid, _) = await DropInLavaAsync(_lavaProofItem);
        await Pair.RunTicksSync(TicksFor(5f));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(itemUid), Is.False);
            Assert.That(SEntMan.HasComponent<KsItemBurningComponent>(itemUid), Is.False, "A lava-proof item must not catch fire.");
        });
    }

    [Test]
    public async Task UnfiredSpearIsForgedByLava()
    {
        var (spearUid, _) = await DropInLavaAsync(UnfiredSpear);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(spearUid), Is.True, "The unfired spear must be replaced.");
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>().Any(meta => meta.EntityPrototype?.ID == FiredSpear.Id), Is.True,
                "Lava must forge it into the finished giantslayer spear.");
        });
    }
}
