#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenLowVoltageRouterTests
{
    [Test]
    public void ABranchDetoursAroundAnAvoidableOccupiedSquare()
    {
        var allowed = Rectangle(4, 4); var roots = new HashSet<Vector2i> { new(0, 0), new(1, 0), new(0, 1) };
        var meter = new Meter(); var router = Create(allowed, roots, meter);
        Assert.That(router.TryJoin(new HashSet<Vector2i> { new(2, 1) }, out var issue), Is.True, issue);
        Assert.That(router.Cells, Does.Contain(new Vector2i(2, 1)));
        Assert.That(router.Cells, Does.Not.Contain(new Vector2i(1, 1)));
        Assert.That(Blocks(router.Cells), Is.Empty);
        ConnectedToRoots(router.Cells, roots);
        Assert.That(router.TrySquareProofs(out var proofs, out issue), Is.True, issue); Assert.That(proofs, Is.Empty);
    }

    [TestCase(false)] [TestCase(true)]
    public void FourMandatoryTerminalsHaveCheckableNecessityOrAreStrictlyForbidden(bool forbid)
    {
        var roots = Rectangle(2, 2); var meter = new Meter();
        var accepted = KsProcgenLowVoltageRouter.TryCreate(Rectangle(4, 4), roots,
            forbid ? KsProcgenElectricalSquarePolicy.ForbidNew : KsProcgenElectricalSquarePolicy.RequireNecessityCertificate,
            meter.Spend, out var router, out var issue);
        Assert.That(accepted, Is.EqualTo(!forbid));
        if (forbid) { Assert.That(router, Is.Null); Assert.That(issue, Is.EqualTo("ElectricalLvSquareForbidden")); return; }
        Assert.That(router!.TrySquareProofs(out var proofs, out issue), Is.True, issue);
        var proof = proofs.Single();
        Assert.That(proof.Minimum, Is.EqualTo(Vector2i.Zero));
        Assert.That(proof.MandatoryTerminals, Is.EquivalentTo(roots));
        Assert.That(proof.CandidateDomainHash, Is.EqualTo(router.CandidateDomainHash));
        Assert.That(proof.TerminalHash, Is.EqualTo(router.TerminalHash));
        Assert.That(router.TryJoin(new HashSet<Vector2i> { new(3, 3) }, out issue), Is.True, issue);
        Assert.That(router.TrySquareProofs(out proofs, out issue), Is.True, issue);
        Assert.That(proofs.Count, Is.EqualTo(1)); Assert.That(Blocks(router.Cells).Count, Is.EqualTo(1));
    }

    [Test]
    public void AHeuristicFailureCannotInventASquareExceptionOrPublishABranch()
    {
        var roots = new HashSet<Vector2i> { new(0, 0), new(1, 0), new(0, 1) };
        var router = Create(Rectangle(2, 2), roots, new Meter()); var before = router.Cells.ToArray();
        Assert.That(router.TryJoin(new HashSet<Vector2i> { new(1, 1) }, out var issue), Is.False);
        Assert.That(issue, Is.EqualTo("ElectricalLvRouteCandidateUnavailable"));
        Assert.That(router.Cells, Is.EqualTo(before));
        Assert.That(router.TrySquareProofs(out var proofs, out issue), Is.True, issue); Assert.That(proofs, Is.Empty);
    }

    [Test]
    public void OneLessWorkUnitCannotPublishAPartialPath()
    {
        var roots = new HashSet<Vector2i> { new(0, 0) }; var target = new HashSet<Vector2i> { new(4, 4) };
        var full = new Meter(); var complete = Create(Rectangle(5, 5), roots, full);
        Assert.That(complete.TryJoin(target, out var issue), Is.True, issue);
        var shortfall = new Meter { Maximum = full.Used - 1 }; var limited = Create(Rectangle(5, 5), roots, shortfall);
        Assert.That(limited.TryJoin(target, out issue), Is.False);
        Assert.That(issue, Is.EqualTo("ElectricalRoutingWorkBudget"));
        Assert.That(limited.Cells, Is.EquivalentTo(roots)); Assert.That(shortfall.Used, Is.EqualTo(shortfall.Maximum));
    }

    [Test]
    public void ProofWorkExhaustionLeavesNoPartialCertificate()
    {
        var meter = new Meter(); var router = Create(Rectangle(4, 4), Rectangle(2, 2), meter);
        meter.Maximum = meter.Used;
        Assert.That(router.TrySquareProofs(out var proofs, out var issue), Is.False);
        Assert.That(issue, Is.EqualTo("ElectricalRoutingWorkBudget")); Assert.That(proofs, Is.Empty);
    }

    [Test]
    public void CallerMutationAndInputOrderCannotChangeTheFrozenRoutingDomain()
    {
        var allowed = Rectangle(5, 5); var roots = new HashSet<Vector2i> { new(0, 0) }; var target = new HashSet<Vector2i> { new(4, 4) };
        var first = Create(allowed, roots, new Meter());
        var replay = Create(allowed.Reverse().ToHashSet(), roots.Reverse().ToHashSet(), new Meter());
        allowed.Clear(); roots.Clear();
        Assert.That(first.TryJoin(target, out var issue), Is.True, issue);
        Assert.That(replay.TryJoin(target, out issue), Is.True, issue);
        Assert.That(first.Cells, Is.EqualTo(replay.Cells));
        Assert.That(first.CandidateDomainHash, Is.EqualTo(replay.CandidateDomainHash));
        Assert.That(first.TerminalHash, Is.EqualTo(replay.TerminalHash));
    }

    [Test]
    public void DiagonalOnlyContactsCannotBecomeWireConnections()
    {
        var allowed = new HashSet<Vector2i> { new(0, 0), new(1, 1) }; var roots = new HashSet<Vector2i> { new(0, 0) };
        var router = Create(allowed, roots, new Meter());
        Assert.That(router.TryJoin(new HashSet<Vector2i> { new(1, 1) }, out var issue), Is.False);
        Assert.That(issue, Is.EqualTo("ElectricalLvRouteCandidateUnavailable")); Assert.That(router.Cells, Is.EquivalentTo(roots));
    }

    [TestCase(1, 1024)] [TestCase(1024, 1)]
    public void LongThinRoutesFitAWorkAllowanceLinearInTheirDomain(int width, int height)
    {
        var meter = new Meter { Maximum = 100_000 }; var roots = new HashSet<Vector2i> { new(0, 0) };
        var router = Create(Rectangle(width, height), roots, meter);
        Assert.That(router.TryJoin(new HashSet<Vector2i> { new(width - 1, height - 1) }, out var issue), Is.True, issue);
        Assert.That(router.Cells.Count, Is.EqualTo(1024));
        Assert.That(router.TrySquareProofs(out var proofs, out issue), Is.True, issue);
        Assert.That(proofs, Is.Empty); Assert.That(meter.Used, Is.LessThanOrEqualTo(meter.Maximum));
        ConnectedToRoots(router.Cells, roots);
    }

    [Test]
    public void EveryThreeByThreeDomainAndMandatoryTerminalSetMatchesAnExhaustivePathOracle()
    {
        var points = Enumerable.Range(0, 9).Select(index => new Vector2i(index % 3, index / 3)).ToArray();
        var neighbors = points.Select(point => Enumerable.Range(0, 9).Where(index =>
            System.Math.Abs(points[index].X - point.X) + System.Math.Abs(points[index].Y - point.Y) == 1).ToArray()).ToArray();
        var squares = new[] { 0, 1, 3, 4 }.Select(index => (1 << index) | (1 << (index + 1)) |
            (1 << (index + 3)) | (1 << (index + 4))).ToArray();
        var domains = 0; var joins = 0;
        for (var allowedMask = 1; allowedMask < 512; allowedMask++)
        for (var rootsMask = allowedMask; rootsMask != 0; rootsMask = (rootsMask - 1) & allowedMask)
        {
            domains++;
            // Independent oracle enumerates simple paths, retaining every distinct occupied set
            // and endpoint. It does not use the router's visited-cell or square helper code.
            var reachable = rootsMask; var visited = new HashSet<(int Occupied, int Cell)>();
            var queue = new Queue<(int Occupied, int Cell)>();
            for (var index = 0; index < 9; index++)
                if ((rootsMask & (1 << index)) != 0) queue.Enqueue((rootsMask, index));
            while (queue.TryDequeue(out var state))
            {
                if (!visited.Add(state)) continue;
                reachable |= 1 << state.Cell;
                foreach (var next in neighbors[state.Cell])
                {
                    var bit = 1 << next;
                    if ((allowedMask & bit) == 0 || (state.Occupied & bit) != 0) continue;
                    var occupied = state.Occupied | bit;
                    if (squares.Any(square => (occupied & square) == square && (rootsMask & square) != square)) continue;
                    queue.Enqueue((occupied, next));
                }
            }
            var allowed = Enumerable.Range(0, 9).Where(index => (allowedMask & (1 << index)) != 0).Select(index => points[index]).ToHashSet();
            var roots = Enumerable.Range(0, 9).Where(index => (rootsMask & (1 << index)) != 0).Select(index => points[index]).ToHashSet();
            for (var target = 0; target < 9; target++)
            {
                var bit = 1 << target;
                if ((allowedMask & bit) == 0 || (rootsMask & bit) != 0) continue;
                joins++;
                var router = Create(allowed, roots, new Meter());
                var accepted = router.TryJoin(new HashSet<Vector2i> { points[target] }, out var issue);
                Assert.That(accepted, Is.EqualTo((reachable & bit) != 0), $"domain={allowedMask}, roots={rootsMask}, target={target}: {issue}");
                if (!accepted) { Assert.That(router.Cells, Is.EquivalentTo(roots)); continue; }
                Assert.That(router.Cells, Does.Contain(points[target]));
                var resultMask = Enumerable.Range(0, 9).Where(index => router.Cells.Contains(points[index])).Aggregate(0, (mask, index) => mask | (1 << index));
                Assert.That(squares.Any(square => (resultMask & square) == square && (rootsMask & square) != square), Is.False);
                ConnectedToRoots(router.Cells, roots);
            }
        }
        Assert.That(domains, Is.EqualTo(19_171)); Assert.That(joins, Is.EqualTo(56_745));
    }

    private sealed class Meter
    {
        public int Used;
        public int Maximum = 1_000_000;
        public bool Spend() { if (Used >= Maximum) return false; Used++; return true; }
    }
    private static KsProcgenLowVoltageRouter Create(IReadOnlySet<Vector2i> allowed, IReadOnlySet<Vector2i> roots, Meter meter)
    {
        Assert.That(KsProcgenLowVoltageRouter.TryCreate(allowed, roots, KsProcgenElectricalSquarePolicy.RequireNecessityCertificate,
            meter.Spend, out var router, out var issue), Is.True, issue);
        return router!;
    }
    private static HashSet<Vector2i> Rectangle(int width, int height) => Enumerable.Range(0, width)
        .SelectMany(x => Enumerable.Range(0, height).Select(y => new Vector2i(x, y))).ToHashSet();
    private static List<Vector2i> Blocks(IEnumerable<Vector2i> cells)
    {
        var set = cells.ToHashSet();
        return set.Where(cell => set.Contains(cell + new Vector2i(1, 0)) && set.Contains(cell + new Vector2i(0, 1)) && set.Contains(cell + Vector2i.One)).ToList();
    }
    private static void ConnectedToRoots(IEnumerable<Vector2i> cells, IReadOnlySet<Vector2i> roots)
    {
        var set = cells.ToHashSet(); var visited = new HashSet<Vector2i>(); var queue = new Queue<Vector2i>(roots);
        while (queue.TryDequeue(out var cell))
        {
            if (!visited.Add(cell)) continue;
            foreach (var step in new[] { new Vector2i(1, 0), new(0, 1), new(-1, 0), new(0, -1) })
                if (set.Contains(cell + step) && !visited.Contains(cell + step)) queue.Enqueue(cell + step);
        }
        Assert.That(visited, Is.EquivalentTo(set));
    }
}
