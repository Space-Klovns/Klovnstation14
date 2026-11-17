#nullable enable
using System;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Storage;
using Content.Server.Gravity;
using Content.Shared.Gravity;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Throwing;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Storage;

/// <summary>
///     Someone thrown into an open closet lands inside it, and it shuts on them. A closed closet catches nobody, and an
///         open one lets a thrown item be.
/// </summary>
[TestFixture]
[TestOf(typeof(KsEntityStorageCatchThrownSystem))]
public sealed class KsEntityStorageCatchThrownTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId Item = "Wrench";
    private readonly EntProtoId _closet = "KsCatchThrownTestCloset";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: ClosetBase
  id: KsCatchThrownTestCloset
  components:
  - type: KsEntityStorageCatchThrown
";

    /// <param name="throwMob">Whether a person is thrown, or an item.</param>
    /// <param name="open">Whether the closet stands open when they reach it.</param>
    /// <param name="expectCaught">Whether they must end up inside, with the closet shut.</param>
    [TestCase(true, true, true)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    public async Task OpenClosetCatchesThrownMobs(bool throwMob, bool open, bool expectCaught)
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid closetUid = default;
        EntityUid thrownUid = default;

        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            for (var x = -1; x < 9; x++)
            {
                mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, 0), testMap.Tile.Tile);
            }

            var gravityComponent = SEntMan.EnsureComponent<GravityComponent>(testMap.Grid.Owner);
            SEntMan.System<GravitySystem>().EnableGravity(testMap.Grid.Owner, gravityComponent);

            closetUid = SEntMan.SpawnEntity(_closet, new EntityCoordinates(testMap.Grid, 3.5f, 0.5f));
            thrownUid = SEntMan.SpawnEntity(throwMob ? Human : Item, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            if (open)
                SEntMan.System<SharedEntityStorageSystem>().OpenStorage(closetUid);
        });

        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<EntityStorageComponent>(closetUid).Open, Is.EqualTo(open));
            SEntMan.System<ThrowingSystem>().TryThrow(thrownUid, new EntityCoordinates(testMap.Grid, 7.5f, 0.5f));
        });

        var timing = Server.ResolveDependency<IGameTiming>();
        await Pair.RunTicksSync((int)Math.Ceiling(2.5 * timing.TickRate));

        await Server.WaitAssertion(() =>
        {
            var storage = SEntMan.GetComponent<EntityStorageComponent>(closetUid);
            var inside = storage.Contents.Contains(thrownUid);
            var what = throwMob ? "someone" : "an item";
            var state = open ? "open" : "closed";

            Assert.That(inside, Is.EqualTo(expectCaught),
                $"A{(open ? "n" : "")} {state} closet must{(expectCaught ? "" : " not")} catch {what} thrown into it.");
            Assert.That(storage.Open, Is.EqualTo(open && !expectCaught),
                expectCaught ? "The closet must shut on whoever it catches." : $"The closet must stay {state}.");
        });
    }
}
