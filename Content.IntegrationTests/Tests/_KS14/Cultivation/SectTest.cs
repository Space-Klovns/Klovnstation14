using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Components;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared._KS14.Cultivation.Jianghu;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     Sects, their missions and rivalries, tournaments and the Martial World Ranking.
/// </summary>
[TestFixture]
[TestOf(typeof(JianghuSystem))]
public sealed class SectTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private EntityUid SpawnPerson(EntityCoordinates coordinates)
    {
        var mindSystem = SEntMan.System<SharedMindSystem>();
        var personUid = SEntMan.SpawnEntity("MobHuman", coordinates);
        mindSystem.TransferTo(mindSystem.CreateMind(null).Owner, personUid);
        return personUid;
    }

    [Test]
    public void TestSectNames()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JianghuSystem.CleanSectName("Azure Cloud"), Is.EqualTo("Azure Cloud Sect"));
            Assert.That(JianghuSystem.CleanSectName("Seven Mops Pavilion"), Is.EqualTo("Seven Mops Pavilion"), "A name that says what it is was changed.");
            Assert.That(JianghuSystem.CleanSectName("[color=red]   [/color]"), Is.Null, "A name of nothing but markup was accepted.");
        }
    }

    [Test]
    public async Task TestFoundingAndRecruitMission()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var jianghuSystem = SEntMan.System<JianghuSystem>();
            var masterUid = SpawnPerson(map.GridCoords);
            var discipleUid = SpawnPerson(map.GridCoords);

            var sect = jianghuSystem.FoundSect(masterUid, "Iron Toolbox Sect");
            Assert.That(sect, Is.Not.Null);
            jianghuSystem.IssueMission(sect!.Value, mission: SectMission.Recruit, goal: 1);
            jianghuSystem.AddMember(sect.Value, discipleUid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(jianghuSystem.IsSectMaster(masterUid), Is.True);
                Assert.That(jianghuSystem.GetSect(discipleUid)?.Owner, Is.EqualTo(sect.Value.Owner), "The disciple did not join.");
                Assert.That(jianghuSystem.SectRank(sect.Value, discipleUid), Is.EqualTo("jianghu-rank-outer"));
                Assert.That(SEntMan.EntityExists(sect.Value.Comp.Plaque), Is.True, "No plaque was hung.");
                Assert.That(sect.Value.Comp.MissionsCompleted, Is.EqualTo(1), "Recruiting did not finish the recruit mission.");
                Assert.That(jianghuSystem.GetFace(discipleUid), Is.EqualTo(3), "A finished mission gave no face.");
            }
        });
    }

    [Test]
    public async Task TestRivalsAndTournamentRaiseTheStakes()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var jianghuSystem = SEntMan.System<JianghuSystem>();
            var winnerUid = SpawnPerson(map.GridCoords);
            var loserUid = SpawnPerson(map.GridCoords);

            var winnerSect = jianghuSystem.FoundSect(winnerUid, "Azure Cloud Sect")!.Value;
            var loserSect = jianghuSystem.FoundSect(loserUid, "Seven Mops Pavilion")!.Value;
            jianghuSystem.DeclareRivalry(winnerSect, loserSect);
            Assert.That(jianghuSystem.TryProclaimTournament(winnerSect, winnerUid), Is.True);

            jianghuSystem.StartDuel(winnerUid, loserUid);
            jianghuSystem.FinishDuel(winnerUid, loserUid, winnerUid: winnerUid, outcome: HonorDuelOutcome.Yielded);
            Assert.That(jianghuSystem.GetFace(winnerUid), Is.EqualTo(20), "A tournament win over a rival was not worth (5 + 5) x 2.");

            jianghuSystem.ConcludeTournament();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(jianghuSystem.Champion, Is.EqualTo(SComp<MindContainerComponent>(winnerUid).Mind), "The winner was not crowned.");
                Assert.That(jianghuSystem.GetFace(winnerUid), Is.EqualTo(30), "The champion's crown gave no face.");
                Assert.That(jianghuSystem.TournamentRunning, Is.False);
            }
        });
    }

    [Test]
    public async Task TestRankingClimbsByDuel()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var jianghuSystem = SEntMan.System<JianghuSystem>();
            var firstUid = SpawnPerson(map.GridCoords);
            var secondUid = SpawnPerson(map.GridCoords);
            var climberUid = SpawnPerson(map.GridCoords);

            void Duel(EntityUid winnerUid, EntityUid loserUid)
            {
                jianghuSystem.StartDuel(winnerUid, loserUid);
                jianghuSystem.FinishDuel(winnerUid, loserUid, winnerUid: winnerUid, outcome: HonorDuelOutcome.Yielded);
            }

            Duel(firstUid, secondUid);
            Duel(secondUid, climberUid);
            Assert.That(jianghuSystem.RankOf(firstUid), Is.EqualTo(1), "Beating the unranked did not place the winner.");
            Assert.That(jianghuSystem.RankOf(secondUid), Is.EqualTo(2));
            Assert.That(jianghuSystem.RefusesChallenge(climberUid, firstUid), Is.True,
                "First Under Heaven answered a challenge from the unranked.");

            Duel(climberUid, firstUid);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(jianghuSystem.RankOf(climberUid), Is.EqualTo(1), "Beating the first did not take their place.");
                Assert.That(jianghuSystem.RankOf(firstUid), Is.EqualTo(2), "The beaten first did not slide down one.");
                Assert.That(jianghuSystem.RankOf(secondUid), Is.EqualTo(3));
            }

            // The same two can't trade places again so soon.
            Duel(firstUid, climberUid);
            Assert.That(jianghuSystem.RankOf(climberUid), Is.EqualTo(1), "Two fighters swapped places twice within the cooldown.");
        });
    }
}
