using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenGeometryTests
{
    [Test]
    public void OneByThreeShapeIsValidAtTileResolution()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "Tiny",
            Shape = new KsProcgenShapeSpec
            {
                AddRectangles =
                [
                    new KsProcgenTileRect { Min = new Vector2i(-2, 4), Max = new Vector2i(-1, 7) },
                ],
            },
        };

        var success = KsProcgenGeometry.TryNormalize(request, out var shape, out var issue);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True, issue?.Message);
            Assert.That(shape?.TargetCells.Count, Is.EqualTo(3));
            Assert.That(shape?.ContainsTarget(new Vector2i(-2, 6)), Is.True);
            Assert.That(shape?.TargetComponents().Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void TextMaskPreservesTopRowHolesAndOwnership()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "Concave",
            Shape = new KsProcgenShapeSpec
            {
                TextOrigin = new Vector2i(10, -5),
                TextMask = "#V \nP##",
                EnvelopeCells = [new Vector2i(13, -5)],
            },
        };

        var success = KsProcgenGeometry.TryNormalize(request, out var shape, out var issue);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True, issue?.Message);
            Assert.That(shape?.ContainsVoid(new Vector2i(11, -4)), Is.True);
            Assert.That(shape?.ContainsTarget(new Vector2i(10, -4)), Is.True);
            Assert.That(shape?.ContainsPreserved(new Vector2i(10, -5)), Is.True);
            Assert.That(shape?.CanWrite(new Vector2i(10, -5)), Is.False);
            Assert.That(shape?.CanWrite(new Vector2i(13, -5)), Is.True);
            Assert.That(shape?.ContainsTarget(new Vector2i(12, -4)), Is.False);
        });
    }

    [Test]
    public void RectangularSubtractionAndDiagonalContactDoNotConnect()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "Islands",
            Shape = new KsProcgenShapeSpec
            {
                AddRectangles =
                [
                    new KsProcgenTileRect { Min = new Vector2i(0, 0), Max = new Vector2i(3, 3) },
                ],
                SubtractRectangles =
                [
                    new KsProcgenTileRect { Min = new Vector2i(1, 0), Max = new Vector2i(3, 2) },
                ],
            },
        };

        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Message);
        Assert.That(shape.TargetCells.Count, Is.EqualTo(5));

        var diagonalRequest = new KsProcgenRequest
        {
            RequestId = "Diagonal",
            Shape = new KsProcgenShapeSpec
            {
                Cells = [new Vector2i(0, 0), new Vector2i(1, 1)],
            },
        };

        Assert.That(KsProcgenGeometry.TryNormalize(diagonalRequest, out var diagonal, out issue), Is.True, issue?.Message);
        Assert.That(diagonal.TargetComponents().Count, Is.EqualTo(2));
    }

    [Test]
    public void InvalidOwnershipAndBudgetFailWithStableCodes()
    {
        var conflicting = new KsProcgenRequest
        {
            RequestId = "Conflict",
            Shape = new KsProcgenShapeSpec
            {
                Cells = [new Vector2i(0, 0)],
                VoidCells = [new Vector2i(0, 0)],
            },
        };

        Assert.That(KsProcgenGeometry.TryNormalize(conflicting, out var shape, out var issue), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(shape, Is.Null);
            Assert.That(issue?.Code, Is.EqualTo("ConflictingMasks"));
        });

        var oversized = new KsProcgenRequest
        {
            RequestId = "Budget",
            Limits = new KsProcgenLimits { MaxCells = 3 },
            Shape = new KsProcgenShapeSpec
            {
                AddRectangles =
                [
                    new KsProcgenTileRect { Min = new Vector2i(0, 0), Max = new Vector2i(2, 2) },
                ],
            },
        };

        Assert.That(KsProcgenGeometry.TryNormalize(oversized, out shape, out issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("CellBudgetExceeded"));
    }

    [Test]
    public void QuarterTurnRotatesAroundNamedAnchor()
    {
        var cell = new Vector2i(4, 1);
        var sourceAnchor = new Vector2i(4, 2);
        var targetAnchor = new Vector2i(-3, 7);

        Assert.Multiple(() =>
        {
            Assert.That(KsProcgenGeometry.TransformCell(cell, sourceAnchor, targetAnchor, 0),
                Is.EqualTo(new Vector2i(-3, 6)));
            Assert.That(KsProcgenGeometry.TransformCell(cell, sourceAnchor, targetAnchor, 1),
                Is.EqualTo(new Vector2i(-4, 7)));
            Assert.That(KsProcgenGeometry.TransformCell(cell, sourceAnchor, targetAnchor, 2),
                Is.EqualTo(new Vector2i(-3, 8)));
            Assert.That(KsProcgenGeometry.TransformCell(cell, sourceAnchor, targetAnchor, 3),
                Is.EqualTo(new Vector2i(-2, 7)));
        });
    }
}
