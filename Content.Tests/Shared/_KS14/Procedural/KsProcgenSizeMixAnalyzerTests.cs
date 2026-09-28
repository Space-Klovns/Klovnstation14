using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenSizeMixAnalyzerTests
{
    [Test]
    public void FinalRoomAreasExcludePassagesAndReportSoftShortfalls()
    {
        KsProcgenThemedRegion Region(string id, int cells, KsProcgenZoneKind kind) => new(
            id, kind, [id], Enumerable.Range(0, cells).Select(x => new Vector2i(x, 0)).ToArray(),
            [], new KsProcgenThemeSelection("theme", "tiles", "palette", "walls", "family",
                null, null, null, []), []);
        var goals = new[]
        {
            new KsProcgenRoomSizeGoal { Id = "large", MinCells = 18, MaxCells = 22, TargetCount = 1 },
            new KsProcgenRoomSizeGoal { Id = "medium", MinCells = 8, MaxCells = 10, TargetCount = 1 },
            new KsProcgenRoomSizeGoal { Id = "small", MinCells = 4, MaxCells = 5, TargetCount = 2 },
        };
        var regions = new[]
        {
            Region("large-room", 22, KsProcgenZoneKind.RoomProposal),
            Region("medium-room", 9, KsProcgenZoneKind.RoomProposal),
            Region("small-room", 4, KsProcgenZoneKind.RoomProposal),
            Region("long-passage", 20, KsProcgenZoneKind.Passage),
        };

        var result = KsProcgenSizeMixAnalyzer.Analyze(goals, regions);

        Assert.That(result.Select(item => (item.GoalId, item.RequestedCount, item.AchievedCount)),
            Is.EqualTo(new[]
            {
                ("large", 1, 1),
                ("medium", 1, 1),
                ("small", 2, 1),
            }));
    }
}
