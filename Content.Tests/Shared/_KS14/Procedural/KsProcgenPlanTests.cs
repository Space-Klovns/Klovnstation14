using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenPlanTests
{
    [Test]
    public void RejectedPackageLeavesNoClaimsOrHashChanges()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "Alternatives",
            Shape = new KsProcgenShapeSpec
            {
                AddRectangles =
                [
                    new KsProcgenTileRect { Min = new Vector2i(0, 0), Max = new Vector2i(2, 2) },
                ],
                PreservedCells = [new Vector2i(0, 0)],
                EnvelopeCells = [new Vector2i(2, 0)],
            },
        };

        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Message);
        var plan = new KsProcgenPlan(shape);
        var beforeHash = plan.SemanticHash(request.RequestId, request.Seed, generatorVersion: 1);
        var checkpoint = plan.Checkpoint();

        Assert.That(plan.TryClaim(new Vector2i(1, 0), new KsProcgenCellClaim("L/A1", KsProcgenCellDisposition.Prefab)), Is.True);
        Assert.That(plan.TryClaim(new Vector2i(0, 1), new KsProcgenCellClaim("L/A2", KsProcgenCellDisposition.Prefab)), Is.True);
        Assert.That(plan.TryClaim(new Vector2i(0, 0), new KsProcgenCellClaim("L/A3", KsProcgenCellDisposition.Prefab)), Is.False);
        Assert.That(plan.TryClaim(new Vector2i(2, 0), new KsProcgenCellClaim("L/Leak", KsProcgenCellDisposition.Prefab)), Is.False);

        plan.Rollback(checkpoint);
        Assert.Multiple(() =>
        {
            Assert.That(plan.SemanticHash(request.RequestId, request.Seed, generatorVersion: 1), Is.EqualTo(beforeHash));
            Assert.That(plan.TryGetClaim(new Vector2i(1, 0), out _), Is.False);
            Assert.That(plan.UnassignedTargetCells().Count, Is.EqualTo(3));
        });

        Assert.That(plan.TryClaim(new Vector2i(1, 0), new KsProcgenCellClaim("Four/B1", KsProcgenCellDisposition.Prefab)), Is.True);
        Assert.That(plan.TryClaim(new Vector2i(0, 1), new KsProcgenCellClaim("Four/B2", KsProcgenCellDisposition.Prefab)), Is.True);
        Assert.That(plan.TryClaim(new Vector2i(1, 1), new KsProcgenCellClaim("Four/B3", KsProcgenCellDisposition.Prefab)), Is.True);
        Assert.That(plan.IsTargetComplete(), Is.True);
    }

    [Test]
    public void PlanHashDoesNotDependOnClaimInsertionOrder()
    {
        var request = new KsProcgenRequest
        {
            RequestId = "StableHash",
            Shape = new KsProcgenShapeSpec
            {
                Cells = [new Vector2i(0, 0), new Vector2i(1, 0)],
            },
        };

        Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out _), Is.True);
        var first = new KsProcgenPlan(shape);
        var second = new KsProcgenPlan(shape);
        var left = new KsProcgenCellClaim("Left", KsProcgenCellDisposition.ProceduralFloor);
        var right = new KsProcgenCellClaim("Right", KsProcgenCellDisposition.Passage);
        first.TryClaim(new Vector2i(0, 0), left);
        first.TryClaim(new Vector2i(1, 0), right);
        second.TryClaim(new Vector2i(1, 0), right);
        second.TryClaim(new Vector2i(0, 0), left);

        Assert.That(first.SemanticHash(request.RequestId, request.Seed, generatorVersion: 1),
            Is.EqualTo(second.SemanticHash(request.RequestId, request.Seed, generatorVersion: 1)));
    }

    [Test]
    public void SplitMix64AndStageStreamsHaveStableVectors()
    {
        var random = new KsProcgenRandom(0);
        Assert.That(random.NextUInt64(), Is.EqualTo(0xE220A8397B1DCDAFUL));
        Assert.That(random.NextUInt64(), Is.EqualTo(0x6E789E6AA1B965F4UL));

        var selection = KsProcgenRandom.ForStage(42, "selection", "West");
        var repeated = KsProcgenRandom.ForStage(42, "selection", "West");
        var decoration = KsProcgenRandom.ForStage(42, "decoration", "West");

        Assert.That(selection.NextUInt64(), Is.EqualTo(repeated.NextUInt64()));
        Assert.That(selection.NextUInt64(), Is.Not.EqualTo(decoration.NextUInt64()));
    }
}
