using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     A heart demon actually goes for the one it came out of.
/// </summary>
[TestFixture]
[TestOf(typeof(HeartDemonSystem))]
public sealed class HeartDemonTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [Test]
    public async Task TestHeartDemonAttacksItsHost()
    {
        var map = await Pair.CreateTestMap();
        EntityUid hostUid = default;

        await Server.WaitAssertion(() =>
        {
            hostUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(SEntMan.System<HeartDemonSystem>().Summon(hostUid), Is.Not.Null, "No heart demon came out.");
        });

        await RunSeconds(10f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(hostUid), Is.GreaterThan(FixedPoint2.Zero),
                "The heart demon never laid a finger on its host.");
        });
    }
}
