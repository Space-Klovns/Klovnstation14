using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Components;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared._KS14.Cultivation.Jianghu;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     Sect swords issued at the plaque, and butterfly swords working in pairs.
/// </summary>
[TestFixture]
[TestOf(typeof(WuxiaWeaponSystem))]
public sealed class WuxiaWeaponTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [Test]
    public async Task TestOneJianPerMember()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var jianghuSystem = SEntMan.System<JianghuSystem>();
            var mindSystem = SEntMan.System<SharedMindSystem>();
            var masterUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            mindSystem.TransferTo(mindSystem.CreateMind(null).Owner, masterUid);

            var sect = jianghuSystem.FoundSect(masterUid, "Iron Toolbox Sect")!.Value;
            var jianUid = jianghuSystem.TryIssueJian(sect, masterUid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(jianUid, Is.Not.Null, "The master got no sword.");
                Assert.That(SComp<SectJianComponent>(jianUid!.Value).Sect, Is.EqualTo(sect.Owner), "The sword doesn't know its sect.");
                Assert.That(jianghuSystem.TryIssueJian(sect, masterUid), Is.Null, "A one-member sect issued two swords.");
            }
        });
    }

    [Test]
    public async Task TestButterflySwordsPair()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var handsSystem = SEntMan.System<SharedHandsSystem>();
            var butterflySystem = SEntMan.System<ButterflySwordSystem>();
            var fighterUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var oneUid = SEntMan.SpawnEntity("KsButterflySword", map.GridCoords);
            var twoUid = SEntMan.SpawnEntity("KsButterflySword", map.GridCoords);

            handsSystem.PickupOrDrop(fighterUid, oneUid, checkActionBlocker: false);
            Assert.That(butterflySystem.IsPaired(oneUid, fighterUid), Is.False, "One sword counted as a pair.");

            handsSystem.PickupOrDrop(fighterUid, twoUid, checkActionBlocker: false);
            Assert.That(butterflySystem.IsPaired(oneUid, fighterUid), Is.True, "Two swords in hand were not a pair.");
        });
    }
}
