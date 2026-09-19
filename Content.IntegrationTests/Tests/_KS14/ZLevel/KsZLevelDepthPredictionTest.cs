#nullable enable
using Content.Shared._KS14.ZLevel;
using Robust.Shared.GameObjects;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.ZLevel;

/// <summary>
///     Depth is networked, and the client scales its render passes and compensates transiting sprites from its
///         own copy of it. So a change has to reach the client, and the shared setter has to work there rather
///         than quietly depending on something server-only - otherwise it could never be predicted.
/// </summary>
public sealed class KsZLevelDepthPredictionTest : KsZLevelTestBase
{
    /// <summary>
    ///     The base fixture runs disconnected; these need a real client to replicate to. Connected defaults to
    ///         false, so it has to be asked for explicitly.
    /// </summary>
    public override PoolSettings PoolSettings => new() { Connected = true };

    /// <summary>
    ///     Puts the player on the stack and returns the client's uid for the z-level they are standing on.
    ///     Nothing replicates to a client that cannot see it, so without this there is no client-side component
    ///         to assert against at all.
    /// </summary>
    private async Task<EntityUid> PutPlayerOnStackAsync(TestStack stack)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();

        var session = Pair.Player;
        Assert.That(session, Is.Not.Null, "these tests need a connected session for anything to replicate to");

        // The pooled pair has a session but no body, so give it one standing on the stack.
        await server.WaitPost(() =>
        {
            var viewerUid = entManager.SpawnEntity(null, stack.SupportedCoords);
            server.PlayerMan.SetAttachedEntity(session, viewerUid);
        });

        await Pair.RunTicksSync(15);

        return Pair.ToClientUid(stack.UpperMapUid);
    }

    [Test]
    public async Task TestDepthReachesTheClient()
    {
        var stack = await CreateStack();
        var clientZLevelUid = await PutPlayerOnStackAsync(stack);

        var server = Pair.Server;
        var client = Pair.Client;
        var serverEntManager = server.ResolveDependency<IEntityManager>();
        var clientEntManager = client.ResolveDependency<IEntityManager>();

        await client.WaitAssertion(() =>
            Assert.That(clientEntManager.GetComponent<KsZLevelComponent>(clientZLevelUid).Depth,
                Is.EqualTo(1f).Within(0.0001f),
                "the client should start out agreeing with the server's default depth"));

        await server.WaitPost(() =>
            Assert.That(serverEntManager.System<KsZLevelSystem>().SetDepth(stack.UpperMapUid, 3f), Is.True,
                "setting a different depth should report that it changed"));

        await Pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
            Assert.That(clientEntManager.GetComponent<KsZLevelComponent>(clientZLevelUid).Depth,
                Is.EqualTo(3f).Within(0.0001f),
                "SetDepth has to dirty the z-level, or the client keeps scaling its render passes by the old depth"));
    }

    /// <summary>
    ///     The prerequisite for prediction: the setter has to run and apply on the client under its own steam.
    /// </summary>
    [Test]
    public async Task TestClientCanApplyDepthItself()
    {
        var stack = await CreateStack();
        var clientZLevelUid = await PutPlayerOnStackAsync(stack);

        var client = Pair.Client;
        var clientEntManager = client.ResolveDependency<IEntityManager>();

        await client.WaitPost(() =>
            clientEntManager.System<KsZLevelSystem>().SetDepth(clientZLevelUid, 4f));

        await client.WaitAssertion(() =>
            Assert.That(clientEntManager.GetComponent<KsZLevelComponent>(clientZLevelUid).Depth,
                Is.EqualTo(4f).Within(0.0001f),
                "the shared setter must apply on the client too, or a predicted caller would see nothing happen"));
    }
}
