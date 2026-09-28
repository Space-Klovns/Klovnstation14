using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenHullBoundaryPlannerTests
{
    [Test]
    public void EnvelopeCoversEveryThinFootprintEdgeWithoutClaimingGasSeal()
    {
        var floor = Enumerable.Range(0, 3).Select(x => new Vector2i(x, 0)).ToArray();
        var envelope = floor.SelectMany(cell => new[]
        {
            cell + new Vector2i(0, 1), cell + new Vector2i(0, -1),
            cell + new Vector2i(1, 0), cell + new Vector2i(-1, 0),
        }).Where(cell => !floor.Contains(cell)).Distinct().ToList();
        var request = new KsProcgenRequest
        {
            RequestId = "ThinWithEnvelope",
            Shape = new KsProcgenShapeSpec { Cells = floor.ToList(), EnvelopeCells = envelope },
        };
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue),
            Is.True, issue?.Message);

        var result = KsProcgenHullBoundaryPlanner.Classify(shape!, request.GeometryMode,
            floor, []);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(KsProcgenHullBoundaryStatus.Classified));
            Assert.That(result.Edges.Count, Is.EqualTo(8));
            Assert.That(result.RequiredEnvelopeCells, Is.EquivalentTo(envelope));
            Assert.That(result.Edges.All(edge => edge.Kind == KsProcgenBoundaryKind.WritableEnvelope),
                Is.True);
            Assert.That(result.GasClosureVerified, Is.False);
        });
    }

    [Test]
    public void MissingFootprintAndUnknownHostRemainUnresolved()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "ThinNoEnvelope",
            Shape = new KsProcgenShapeSpec
            {
                Cells = [new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(2, 0)],
            },
        };
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out _), Is.True);
        var footprint = KsProcgenHullBoundaryPlanner.Classify(shape!, KsProcgenGeometryMode.Footprint,
            request.Shape.Cells, []);
        var host = KsProcgenHullBoundaryPlanner.Classify(shape, KsProcgenGeometryMode.InteriorFill,
            request.Shape.Cells, []);

        Assert.Multiple(() =>
        {
            Assert.That(footprint.Status, Is.EqualTo(KsProcgenHullBoundaryStatus.UnresolvedBoundary));
            Assert.That(footprint.Edges.Count, Is.EqualTo(8));
            Assert.That(footprint.Edges.All(edge => edge.Kind ==
                KsProcgenBoundaryKind.MissingFootprintHull), Is.True);
            Assert.That(host.Status, Is.EqualTo(KsProcgenHullBoundaryStatus.UnresolvedBoundary));
            Assert.That(host.Edges.All(edge => edge.Kind ==
                KsProcgenBoundaryKind.UnknownHostContext), Is.True);
        });
    }

    [Test]
    public void PreservedNeighborAndVoidAreDistinguished()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "BoundaryKinds",
            Shape = new KsProcgenShapeSpec
            {
                Cells = [new Vector2i(0, 0), new Vector2i(1, 0)],
                PreservedCells = [new Vector2i(1, 0)],
                VoidCells = [new Vector2i(0, 1)],
            },
        };
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out _), Is.True);
        var result = KsProcgenHullBoundaryPlanner.Classify(shape!, KsProcgenGeometryMode.Footprint,
            [new Vector2i(0, 0)], []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Edges.Single(edge => edge.BoundaryCell == new Vector2i(1, 0)).Kind,
                Is.EqualTo(KsProcgenBoundaryKind.PreservedUnknown));
            Assert.That(result.Edges.Single(edge => edge.BoundaryCell == new Vector2i(0, 1)).Kind,
                Is.EqualTo(KsProcgenBoundaryKind.ExplicitVoid));
            Assert.That(result.GasClosureVerified, Is.False);
        });
    }
}
