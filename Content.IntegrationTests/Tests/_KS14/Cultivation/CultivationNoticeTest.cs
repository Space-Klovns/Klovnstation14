using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     Messages over the same entity wait their turn instead of being drawn on top of each other.
/// </summary>
[TestFixture]
[TestOf(typeof(CultivationNoticeSystem))]
public sealed class CultivationNoticeTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [Test]
    public async Task TestMessagesTakeTurns()
    {
        var map = await Pair.CreateTestMap();
        EntityUid uid = default;

        await Server.WaitAssertion(() =>
        {
            var noticeSystem = SEntMan.System<CultivationNoticeSystem>();
            uid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);

            noticeSystem.Notify(uid, "first");
            noticeSystem.Announce(uid, "second");
            noticeSystem.Notify(uid, "third");
            Assert.That(noticeSystem.PendingCount(uid), Is.EqualTo(2), "Messages in one tick were not held back.");
        });

        await RunSeconds(1.5f);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<CultivationNoticeSystem>().PendingCount(uid), Is.EqualTo(1), "The queue did not move on in turn."));

        await RunSeconds(1.5f);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<CultivationNoticeSystem>().PendingCount(uid), Is.Zero, "The last message never showed."));
    }
}
