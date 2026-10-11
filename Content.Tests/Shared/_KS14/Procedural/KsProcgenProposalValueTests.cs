using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenProposalValueTests
{
    [Test]
    public void FootprintCopiesInputAndDoesNotExposeMutableArray()
    {
        var cells = new[] { new Vector2i(0, 0), new Vector2i(1, 0) };
        var footprint = new KsProcgenEntityFootprint(cells);
        cells[0] = new(9, 9);
        Assert.That(footprint.Cells[0], Is.EqualTo(new Vector2i(0, 0)));
        Assert.That(footprint.Cells, Is.Not.InstanceOf<Vector2i[]>());
        Assert.That(footprint, Is.EqualTo(new KsProcgenEntityFootprint([new(0, 0), new(1, 0)])));
    }

    [Test]
    public void RelationPathCopiesInputAndRetainsOrderedValueIdentity()
    {
        var cells = new[] { new Vector2i(0, 0), new Vector2i(1, 0) };
        var path = new KsProcgenRelationPath(cells);
        cells[0] = new(9, 9);
        Assert.That(path.Cells[0], Is.EqualTo(new Vector2i(0, 0)));
        Assert.That(path.Cells, Is.Not.InstanceOf<Vector2i[]>());
        Assert.That(path, Is.EqualTo(new KsProcgenRelationPath([new(0, 0), new(1, 0)])));
        Assert.That(path, Is.Not.EqualTo(new KsProcgenRelationPath([new(1, 0), new(0, 0)])));
    }
}
