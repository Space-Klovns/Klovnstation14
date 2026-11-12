#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Voices;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     A Redacted's recorded line gets to a player standing beside it: the clip the server plays on the mob reaches the
///         client. What it sounds like is for listening to: a headless client plays nothing.
/// </summary>
public sealed class KsNpcVoiceRecordingClientTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const string RedactedSpawner = "KsRandomHumanoidSpawnerOperativeRedacted";
    private const string Line = "ks-operative-redacted-line-execution-1";

    [Test]
    public async Task TestALineReachesAPlayerBeside()
    {
        if (!Server.ProtoMan.HasIndex<EntityPrototype>(RedactedSpawner))
            Assert.Ignore("the Redacted are in the private _KsModule submodule, which is not present");

        var map = await Pair.CreateTestMap();

        await Server.WaitPost(() =>
        {
            var listenerUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, listenerUid);
            SEntMan.SpawnAtPosition(RedactedSpawner, map.GridCoords);
        });

        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<NpcVoiceSetComponent>();
            Assert.That(query.MoveNext(out var mobUid, out _), "the spawner should have spawned a voiced Redacted");
            Assert.That(SEntMan.System<NpcVoiceSystem>().TryPlayLine(mobUid, Line), "the server should play the clip");
        });

        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var clientEntityManager = Client.EntMan;
            var audioQuery = clientEntityManager.EntityQueryEnumerator<AudioComponent>();
            var heard = 0;

            while (audioQuery.MoveNext(out _, out var audioComponent))
            {
                if ($"{audioComponent.FileName}".Contains("/Voice/Redacted/"))
                    heard++;
            }

            Assert.That(heard, Is.GreaterThan(0), "the clip should reach the client");
        });
    }
}
