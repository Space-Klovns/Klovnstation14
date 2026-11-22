#nullable enable
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.ZLevel;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.ZLevel;

/// <summary>
///     A player is sent as many z-levels below them as <see cref="KsCCVars.ZLevelPvsLevelsBelow"/> says, and no more.
/// </summary>
/// <remarks>
///     Every pooled pair runs with net.pvs off, which sends every entity to every client, so these turn it back on:
///         without that they would pass whatever the code did.
/// </remarks>
public sealed class KsZLevelPvsLevelsBelowTest : KsZLevelTestBase
{
    public override PoolSettings PoolSettings => new() { Connected = true };

    /// <summary>
    ///     Puts the player on the top of a stack of three, and something on each of the two z-levels under them.
    /// </summary>
    private async Task<(NetEntity OneBelow, NetEntity TwoBelow)> SetUpAsync()
    {
        var stack = await CreateStack();
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();

        var session = Pair.Player;
        Assert.That(session, Is.Not.Null, "this test needs a connected session for anything to replicate to");

        var oneBelow = NetEntity.Invalid;
        var twoBelow = NetEntity.Invalid;

        await server.WaitPost(() =>
        {
            var bottomMapUid = entManager.System<SharedMapSystem>().CreateMap(out var bottomMapId);
            entManager.EnsureComponent<KsZLevelComponent>(bottomMapUid);
            entManager.System<KsZLevelSystem>().AddZLevelDirectlyUnder(stack.LowerMapUid, bottomMapUid);

            var viewerUid = entManager.SpawnEntity(null, stack.SupportedCoords);
            server.PlayerMan.SetAttachedEntity(session, viewerUid);

            // Directly under the viewer, so that being out of range is not why one does not arrive.
            oneBelow = entManager.GetNetEntity(entManager.SpawnEntity(null, stack.UnderFloorCoords));
            twoBelow = entManager.GetNetEntity(entManager.SpawnEntity(null, new MapCoordinates(0.5f, 0.5f, bottomMapId)));
        });

        // Long enough for the PVS sweep, which runs on its own interval rather than every tick.
        await Pair.RunTicksSync(30);

        return (oneBelow, twoBelow);
    }

    [Test]
    public async Task TestOnlyTheLevelDirectlyBelowIsSentByDefault()
    {
        await OverrideCVar(Side.Server, CVars.NetPVS, true);
        await OverrideCVar(Side.Server, KsCCVars.ZLevelPvsLevelsBelow, 1);

        var (oneBelow, twoBelow) = await SetUpAsync();
        var clientEntManager = Pair.Client.ResolveDependency<IEntityManager>();

        await Pair.Client.WaitAssertion(() =>
        {
            Assert.That(clientEntManager.TryGetEntity(oneBelow, out _), Is.True,
                "an entity on the z-level directly below the player should reach the client");
            Assert.That(clientEntManager.TryGetEntity(twoBelow, out _), Is.False,
                "with one level sent below, an entity two z-levels down should not have arrived");
        });
    }

    [Test]
    public async Task TestMoreLevelsBelowAreSentWhenAskedFor()
    {
        await OverrideCVar(Side.Server, CVars.NetPVS, true);
        await OverrideCVar(Side.Server, KsCCVars.ZLevelPvsLevelsBelow, 2);

        var (oneBelow, twoBelow) = await SetUpAsync();
        var clientEntManager = Pair.Client.ResolveDependency<IEntityManager>();

        await Pair.Client.WaitAssertion(() =>
        {
            Assert.That(clientEntManager.TryGetEntity(oneBelow, out _), Is.True,
                "an entity on the z-level directly below the player should reach the client");
            Assert.That(clientEntManager.TryGetEntity(twoBelow, out _), Is.True,
                "with two levels sent below, an entity two z-levels down should reach the client");
        });
    }

    /// <summary>
    ///     At zero, none are.
    /// </summary>
    [Test]
    public async Task TestNoLevelsBelowAreSentAtZero()
    {
        await OverrideCVar(Side.Server, CVars.NetPVS, true);
        await OverrideCVar(Side.Server, KsCCVars.ZLevelPvsLevelsBelow, 0);

        var (oneBelow, twoBelow) = await SetUpAsync();
        var clientEntManager = Pair.Client.ResolveDependency<IEntityManager>();

        await Pair.Client.WaitAssertion(() =>
        {
            Assert.That(clientEntManager.TryGetEntity(oneBelow, out _), Is.False,
                "with no levels sent below, an entity on the z-level directly below should not have arrived");
            Assert.That(clientEntManager.TryGetEntity(twoBelow, out _), Is.False,
                "nor one two z-levels down");
        });
    }
}
