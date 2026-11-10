using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared.CCVar;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.UserInterface;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Storage;

/// <summary>
///     What happens when a player opens more storage windows than the storage limit allows.
/// </summary>
/// <remarks>
///     Upstream refused the new window. This fork evicts the oldest one instead, so opening a container
///         always works.
///     The limit-of-one case is the one worth watching: upstream short-circuits it by closing every
///         storage UI before the eviction path runs, which is what made the replacement window lose the
///         old one's screen position.
/// </remarks>
[TestFixture]
[TestOf(typeof(SharedStorageSystem))]
public sealed class StorageWindowLimitTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestLimitedStorage
  name: test limited storage
  components:
  - type: ContainerContainer
  - type: Storage
    grid:
    - 0,0,3,3
  - type: UserInterface
    interfaces:
      enum.StorageUiKey.Key:
        type: StorageBoundUserInterface

- type: entity
  id: KsTestStorageActor
  name: test storage actor
  components:
  - type: ContainerContainer
  - type: UserInterface
";

    private const string StorageProto = "KsTestLimitedStorage";
    private const string ActorProto = "KsTestStorageActor";

    /// <summary>
    ///     Opens the given number of storages in order, and reports which of them are still open.
    /// </summary>
    private async Task<bool[]> OpenStorages(int count)
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        var storageUids = new EntityUid[count];
        var stillOpen = new bool[count];
        var actorUid = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var storageSystem = entityManager.System<SharedStorageSystem>();

            // The pooled session has no body of its own, and a storage window needs an actor to belong to.
            actorUid = entityManager.Spawn(ActorProto);
            server.PlayerMan.SetAttachedEntity(ServerSession!, actorUid);

            for (var index = 0; index < count; index++)
            {
                storageUids[index] = entityManager.Spawn(StorageProto);
                storageSystem.OpenStorageUI(storageUids[index], actorUid);
            }
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var userInterfaceSystem = entityManager.System<SharedUserInterfaceSystem>();

            for (var index = 0; index < count; index++)
            {
                stillOpen[index] = userInterfaceSystem.IsUiOpen(
                    storageUids[index],
                    StorageComponent.StorageUiKey.Key,
                    actorUid);
            }
        });

        return stillOpen;
    }

    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.StorageLimit), 2)]
    public async Task TestOpeningPastTheLimitEvictsTheOldestWindow()
    {
        var stillOpen = await OpenStorages(3);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stillOpen[0], Is.False, "The oldest storage window was not evicted at the limit.");
            Assert.That(stillOpen[1], "The second storage window was evicted instead of the oldest.");
            Assert.That(stillOpen[2], "The newly opened storage window was refused rather than making room.");
        }
    }

    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.StorageLimit), 1)]
    public async Task TestLimitOfOneKeepsOnlyTheNewestWindow()
    {
        var stillOpen = await OpenStorages(2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stillOpen[0], Is.False, "The previous storage window stayed open at a limit of one.");
            Assert.That(stillOpen[1], "The newly opened storage window was refused at a limit of one.");
        }
    }

    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.StorageLimit), -1)]
    public async Task TestNegativeLimitLeavesEveryWindowOpen()
    {
        var stillOpen = await OpenStorages(3);

        Assert.That(stillOpen, Is.All.True, "A negative storage limit still evicted a window.");
    }

    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.StorageLimit), 0)]
    public async Task TestZeroLimitRefusesEveryWindow()
    {
        var stillOpen = await OpenStorages(2);

        Assert.That(stillOpen, Is.All.False, "A storage limit of zero still let a window open.");
    }
}
