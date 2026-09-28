using System.Linq;
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

    [Test]
    public void NamedConstantTransformsIntoImmutableTargetCells()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "ConstantLandmark",
            Shape = new KsProcgenShapeSpec
            {
                AddRectangles =
                [
                    new KsProcgenTileRect { Min = new Vector2i(0, 0), Max = new Vector2i(4, 4) },
                ],
                PreservedCells = [new Vector2i(0, 0)],
            },
            ConstantRegions =
            [
                new KsProcgenConstantRegionSpec
                {
                    Id = "ArrivalHall",
                    SourceId = "ArrivalHallMap",
                    ContentFingerprint = "sha256:fixture-a",
                    LocalCells = [new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(1, 1)],
                    Origin = new Vector2i(2, 2),
                    QuarterTurns = 1,
                },
            ],
        };

        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue),
            Is.True, issue?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(shape!.PreservedCells.Count, Is.EqualTo(4));
            Assert.That(shape.ConstantRegions.Single().Cells,
                Is.EquivalentTo(new[] { new Vector2i(2, 2), new Vector2i(2, 1), new Vector2i(3, 1) }));
            Assert.That(shape.TryGetConstantOwner(new Vector2i(2, 1), out var owner), Is.True);
            Assert.That(owner, Is.EqualTo("ArrivalHall"));
            Assert.That(shape.CanWrite(new Vector2i(2, 1)), Is.False);
            Assert.That(shape.CanWrite(new Vector2i(0, 0)), Is.False);
            Assert.That(shape.TryGetConstantOwner(new Vector2i(0, 0), out _), Is.False);
        });

        var packing = KsProcgenPackingPlanner.Plan(request, []);
        Assert.That(packing.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady),
            packing.Issue?.Message);
        Assert.That(packing.CellClaims.Count(claim => claim.Claim.OwnerId == "constant:ArrivalHall"),
            Is.EqualTo(3));
        Assert.That(packing.CellClaims.Count(claim => claim.Claim.Disposition ==
            KsProcgenCellDisposition.ProceduralFloor), Is.EqualTo(12));

        request.Seed = 9191;
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var changedSeed, out issue),
            Is.True, issue?.Message);
        Assert.That(changedSeed!.ConstantContractHash, Is.EqualTo(shape.ConstantContractHash));
        request.ConstantRegions[0].ContentFingerprint = "sha256:fixture-b";
        Assert.That(KsProcgenGeometry.TryNormalize(request, out var changedContent, out issue),
            Is.True, issue?.Message);
        Assert.That(changedContent!.ConstantContractHash, Is.Not.EqualTo(shape.ConstantContractHash));
    }

    [Test]
    public void ConstantsRejectOverlapOutsideMaskAndInvalidTransform()
    {
        KsProcgenRequest Request(KsProcgenConstantRegionSpec constant) => new()
        {
            RequestId = "ConstantInvalid",
            Shape = new KsProcgenShapeSpec
            {
                Cells = [new Vector2i(0, 0), new Vector2i(1, 0)],
                PreservedCells = [new Vector2i(0, 0)],
            },
            ConstantRegions = [constant],
        };

        var constant = new KsProcgenConstantRegionSpec
        {
            Id = "Fixed",
            SourceId = "FixedMap",
            ContentFingerprint = "fixture",
            LocalCells = [new Vector2i(0, 0)],
        };
        Assert.That(KsProcgenGeometry.TryNormalize(Request(constant), out _, out var issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("ConstantOverlap"));

        constant.Origin = new Vector2i(2, 0);
        Assert.That(KsProcgenGeometry.TryNormalize(Request(constant), out _, out issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("ConstantOutsideTarget"));

        constant.Origin = new Vector2i(1, 0);
        constant.QuarterTurns = 4;
        Assert.That(KsProcgenGeometry.TryNormalize(Request(constant), out _, out issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("InvalidConstantRegion"));

        constant.QuarterTurns = 0;
        constant.LocalCells = [new Vector2i(0, 0), new Vector2i(0, 0)];
        Assert.That(KsProcgenGeometry.TryNormalize(Request(constant), out _, out issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("DuplicateConstantCell"));
    }

    [Test]
    public void ConstantOnlyCoverStillRequiresInspectedRootPassage()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "ConstantOnly",
            Mode = KsProcgenMode.Prefabs,
            Shape = new KsProcgenShapeSpec { Cells = [new Vector2i(5, 5)] },
            ConstantRegions =
            [
                new KsProcgenConstantRegionSpec
                {
                    Id = "Arrival",
                    SourceId = "ArrivalMap",
                    ContentFingerprint = "fixture",
                    LocalCells = [new Vector2i(0, 0)],
                    Origin = new Vector2i(5, 5),
                },
            ],
        };

        var fixedOnly = KsProcgenPackingPlanner.Plan(request, []);
        Assert.That(fixedOnly.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady),
            fixedOnly.Issue?.Message);
        Assert.That(fixedOnly.CellClaims.Single().Claim.OwnerId, Is.EqualTo("constant:Arrival"));

        request.RootCells = [new Vector2i(5, 5)];
        var uninspected = KsProcgenPackingPlanner.Plan(request, []);
        Assert.That(uninspected.Status, Is.EqualTo(KsProcgenPackingStatus.NoPreliminaryRoute));
        var inspected = KsProcgenPackingPlanner.Plan(request, [],
            inspectedExistingPassages: new[] { new Vector2i(5, 5) }.ToHashSet());
        Assert.That(inspected.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady),
            inspected.Issue?.Message);
    }

    [Test]
    public void ConstantPortsRouteToResidualOrMatchAnOppositeConstantPort()
    {
        var first = new KsProcgenConstantRegionSpec
        {
            Id = "West",
            SourceId = "WestMap",
            ContentFingerprint = "west",
            LocalCells = [new Vector2i(0, 0), new Vector2i(1, 0)],
            Ports =
            [
                new KsProcgenConstantPortSpec
                {
                    Id = "EastDoor",
                    Threshold = new Vector2i(1, 0),
                    OutwardNormal = new Vector2i(1, 0),
                },
            ],
        };
        var request = new KsProcgenRequest
        {
            RequestId = "ConstantPort",
            Shape = new KsProcgenShapeSpec
            {
                Cells = [new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(2, 0),
                    new Vector2i(3, 0)],
            },
            ConstantRegions = [first],
        };
        var toResidual = KsProcgenPackingPlanner.Plan(request, []);
        Assert.That(toResidual.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady),
            toResidual.Issue?.Message);
        Assert.That(toResidual.ResidualRouting?.ReservedPassageCells,
            Does.Contain(new Vector2i(2, 0)));

        var second = new KsProcgenConstantRegionSpec
        {
            Id = "East",
            SourceId = "EastMap",
            ContentFingerprint = "east",
            Origin = new Vector2i(2, 0),
            LocalCells = [new Vector2i(0, 0), new Vector2i(1, 0)],
        };
        request.ConstantRegions.Add(second);
        var blocked = KsProcgenPackingPlanner.Plan(request, []);
        Assert.That(blocked.Status, Is.EqualTo(KsProcgenPackingStatus.NoPreliminaryRoute));
        Assert.That(blocked.Issue?.Code, Is.EqualTo("PortLandingUnavailable"));

        second.Ports =
        [
            new KsProcgenConstantPortSpec
            {
                Id = "WestDoor",
                Threshold = new Vector2i(0, 0),
                OutwardNormal = new Vector2i(-1, 0),
            },
        ];
        var paired = KsProcgenPackingPlanner.Plan(request, []);
        Assert.That(paired.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady),
            paired.Issue?.Message);
        Assert.That(paired.ResidualRouting?.DirectPortPairs.Count, Is.EqualTo(1));
    }

    [Test]
    public void ConstantPortRequiresInteriorLandingAndTargetSide()
    {
        var port = new KsProcgenConstantPortSpec
        {
            Id = "Door",
            Threshold = new Vector2i(0, 0),
            OutwardNormal = new Vector2i(1, 0),
        };
        var constant = new KsProcgenConstantRegionSpec
        {
            Id = "Fixed",
            SourceId = "FixedMap",
            ContentFingerprint = "fixed",
            LocalCells = [new Vector2i(0, 0)],
            Ports = [port],
        };
        var request = new KsProcgenRequest
        {
            RequestId = "InvalidConstantPort",
            Shape = new KsProcgenShapeSpec
            {
                Cells = [new Vector2i(0, 0), new Vector2i(1, 0)],
            },
            ConstantRegions = [constant],
        };
        Assert.That(KsProcgenGeometry.TryNormalize(request, out _, out var issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("InvalidConstantPort"));

        constant.LocalCells = [new Vector2i(-1, 0), new Vector2i(0, 0)];
        request.Shape.Cells.Add(new Vector2i(-1, 0));
        request.Shape.Cells.Remove(new Vector2i(1, 0));
        Assert.That(KsProcgenGeometry.TryNormalize(request, out _, out issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("ConstantPortOutsideTarget"));
    }

    [Test]
    public void SizeMixRejectsMalformedAndUnboundedGoals()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "SizeMixValidation",
            Shape = new KsProcgenShapeSpec { Cells = [new Vector2i(0, 0)] },
            SizeMix =
            [
                new KsProcgenRoomSizeGoal { Id = "small", MinCells = 0, MaxCells = 4 },
            ],
        };
        Assert.That(KsProcgenGeometry.TryNormalize(request, out _, out var issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("InvalidSizeMix"));

        request.SizeMix[0].MinCells = 1;
        request.SizeMix[0].TargetCount = 4097;
        Assert.That(KsProcgenGeometry.TryNormalize(request, out _, out issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("SizeMixBudget"));

        request.SizeMix[0].TargetCount = 1;
        request.SizeMix.Add(new KsProcgenRoomSizeGoal { Id = "small", MinCells = 1 });
        Assert.That(KsProcgenGeometry.TryNormalize(request, out _, out issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("InvalidSizeMix"));

        request.SizeMix[1].Id = "medium";
        request.SizeMix[1].MinCells = 3;
        request.SizeMix[0].MaxCells = 4;
        Assert.That(KsProcgenGeometry.TryNormalize(request, out _, out issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("OverlappingSizeMix"));
    }

    [Test]
    public void WindowGoalRejectsInvalidFractionAndCountBounds()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "WindowGoalValidation",
            Shape = new KsProcgenShapeSpec { Cells = [new Vector2i(0, 0)] },
            WindowGoal = new KsProcgenWindowGoal { ExteriorWindowFraction = float.NaN },
        };
        Assert.That(KsProcgenGeometry.TryNormalize(request, out _, out var issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("InvalidWindowGoal"));

        request.WindowGoal.ExteriorWindowFraction = 0.25f;
        request.WindowGoal.MinimumCount = 3;
        request.WindowGoal.MaximumCount = 2;
        Assert.That(KsProcgenGeometry.TryNormalize(request, out _, out issue), Is.False);
        Assert.That(issue?.Code, Is.EqualTo("InvalidWindowGoal"));

        request.WindowGoal.MaximumCount = 3;
        Assert.That(KsProcgenGeometry.TryNormalize(request, out _, out issue), Is.True,
            issue?.Message);
    }
}
