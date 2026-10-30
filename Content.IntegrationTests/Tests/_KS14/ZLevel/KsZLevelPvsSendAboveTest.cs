#nullable enable
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.ZLevel;

/// <summary>
///     The z-level above a player is never rendered and never sent, so light can only fall onto them from it
///         if something goes out of its way to send it. This is that something.
/// </summary>
/// <remarks>
///     Every pooled pair runs with net.pvs off (PoolManager), which sends every entity to every client - so
///         without turning it back on these tests would pass no matter what the code did. Turning it on is
///         the whole point of the fixture.
/// </remarks>
public sealed class KsZLevelPvsSendAboveTest : KsZLevelTestBase
{
    public override PoolSettings PoolSettings => new() { Connected = true };

    /// <summary>
    ///     Puts the player on the bottom of the stack, so that there is a z-level above them at all, and puts
    ///         something on the z-level above for the client to either receive or not.
    /// </summary>
    private async Task<NetEntity> SetUpAsync(TestStack stack)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();

        var session = Pair.Player;
        Assert.That(session, Is.Not.Null, "this test needs a connected session for anything to replicate to");

        var netEntity = NetEntity.Invalid;

        await server.WaitPost(() =>
        {
            // Deliberately the lower z-level: standing on the top of the stack there is nothing overhead, and
            //      the feature would correctly do nothing.
            var viewerUid = entManager.SpawnEntity(null, stack.UnderFloorCoords);
            server.PlayerMan.SetAttachedEntity(session, viewerUid);

            netEntity = entManager.GetNetEntity(entManager.SpawnEntity(null, stack.SupportedCoords));
        });

        // Long enough for the PVS sweep, which runs on its own interval rather than every tick.
        await Pair.RunTicksSync(30);

        return netEntity;
    }

    [Test]
    public async Task TestAboveIsSentWhenAskedFor()
    {
        await OverrideCVar(Side.Server, CVars.NetPVS, true);
        await OverrideCVar(Side.Server, KsCCVars.ZLevelPvsSendAbove, true);

        var stack = await CreateStack();
        var netEntity = await SetUpAsync(stack);

        var client = Pair.Client;
        var clientEntManager = client.ResolveDependency<IEntityManager>();

        await client.WaitAssertion(() =>
            Assert.That(clientEntManager.TryGetEntity(netEntity, out _), Is.True,
                "with pvs_send_above on, an entity on the z-level above the player should reach the client"));
    }

    /// <summary>
    ///     The other half: without this, the test above could pass because something else sends it anyway, and
    ///         would prove nothing about the cvar.
    /// </summary>
    [Test]
    public async Task TestAboveIsNotSentOtherwise()
    {
        await OverrideCVar(Side.Server, CVars.NetPVS, true);
        await OverrideCVar(Side.Server, KsCCVars.ZLevelPvsSendAbove, false);

        var stack = await CreateStack();
        var netEntity = await SetUpAsync(stack);

        var client = Pair.Client;
        var clientEntManager = client.ResolveDependency<IEntityManager>();

        await client.WaitAssertion(() =>
            Assert.That(clientEntManager.TryGetEntity(netEntity, out _), Is.False,
                "nothing else sends the z-level above, so with the cvar off it should not have arrived"));
    }
}
