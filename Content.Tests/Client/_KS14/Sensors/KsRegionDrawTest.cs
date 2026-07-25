using System.Collections.Generic;
using System.Numerics;
using Content.Client._KS14.Shuttles.UI;
using Content.Shared._KS14.Sensors;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests.Client._KS14.Sensors;

/// <summary>
///     Framing rules for the coverage-region vertex builder: a fan's world offsets
///         ride the apex through only the view's linear part, while a wedge stays
///         fully grid-framed.
/// </summary>
[TestFixture]
public sealed class KsRegionDrawTest
{
    private static KsSensorRegionState MakeRegion(List<Vector2> points, bool worldOffsets = true)
    {
        return new KsSensorRegionState
        {
            Sensor = new NetEntity(7),
            WorldOffsets = worldOffsets,
            Points = points,
        };
    }

    [Test]
    public void BuildVertsFramesWorldOffsetsAgainstTheApex()
    {
        var region = MakeRegion([new Vector2(1f, 2f), new Vector2(10f, 0f)]);

        // grid -> view: translate; world -> view: pure scale with the Y flip the
        // minimap uses. TransformNormal must apply only the linear part.
        var gridToView = Matrix3x2.CreateTranslation(100f, 50f);
        var worldToView = Matrix3x2.CreateScale(2f, -2f) * Matrix3x2.CreateTranslation(999f, 999f);

        var verts = KsRegionDraw.BuildVerts(region, gridToView, worldToView);

        var apex = new Vector2(101f, 52f);
        Assert.That(verts[0], Is.EqualTo(apex));
        Assert.That(verts[1], Is.EqualTo(apex + new Vector2(20f, 0f)),
            "a world offset must scale/rotate with the view but never pick up its translation");
    }

    [Test]
    public void BuildVertsKeepsAWedgeFullyGridFramed()
    {
        var region = MakeRegion([new Vector2(1f, 2f), new Vector2(10f, 0f)], worldOffsets: false);

        var gridToView = Matrix3x2.CreateTranslation(100f, 50f);
        var worldToView = Matrix3x2.CreateScale(2f, -2f);

        var verts = KsRegionDraw.BuildVerts(region, gridToView, worldToView);

        Assert.That(verts[1], Is.EqualTo(new Vector2(110f, 50f)));
    }
}
