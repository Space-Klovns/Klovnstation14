using System.Numerics;
using Content.Client._KS14.Lobby;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Lobby;

[TestFixture]
[TestOf(typeof(KsLobbyContrastMath))]
public sealed class KsLobbyContrastMathTests
{
    private static void AssertRect(Vector4 actual, float minX, float minY, float maxX, float maxY)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.X, Is.EqualTo(minX).Within(0.0001f), "minX");
            Assert.That(actual.Y, Is.EqualTo(minY).Within(0.0001f), "minY");
            Assert.That(actual.Z, Is.EqualTo(maxX).Within(0.0001f), "maxX");
            Assert.That(actual.W, Is.EqualTo(maxY).Within(0.0001f), "maxY");
        });
    }

    [Test]
    public void FullScreenQuadMapsToFractionsOfTheScreen()
    {
        var quad = UIBox2.FromDimensions(0f, 0f, 1000f, 500f);
        var region = UIBox2.FromDimensions(100f, 50f, 200f, 100f);

        AssertRect(KsLobbyContrastMath.RegionToQuadSpace(region, quad, paddingPixels: 0f),
            0.1f, 0.1f, 0.3f, 0.3f);
    }

    [Test]
    public void PaddingGrowsTheRegionOnEverySide()
    {
        var quad = UIBox2.FromDimensions(0f, 0f, 1000f, 500f);
        var region = UIBox2.FromDimensions(100f, 50f, 200f, 100f);

        AssertRect(KsLobbyContrastMath.RegionToQuadSpace(region, quad, paddingPixels: 10f),
            0.09f, 0.08f, 0.31f, 0.32f);
    }

    [Test]
    public void WiderScreenThanPictureLetterboxesHorizontally()
    {
        // A 4:3 picture drawn into a 2000x1000 control occupies x 333..1667; the quad starts at its left edge.
        var quad = UIBox2.FromDimensions(333.3333f, 0f, 1333.3333f, 1000f);
        var region = UIBox2.FromDimensions(333.3333f + 133.33333f, 100f, 266.66667f, 200f);

        AssertRect(KsLobbyContrastMath.RegionToQuadSpace(region, quad, paddingPixels: 0f),
            0.1f, 0.1f, 0.3f, 0.3f);
    }

    [Test]
    public void TallerScreenThanPictureLetterboxesVertically()
    {
        var quad = UIBox2.FromDimensions(0f, 250f, 1000f, 500f);
        var region = UIBox2.FromDimensions(100f, 250f + 50f, 200f, 100f);

        AssertRect(KsLobbyContrastMath.RegionToQuadSpace(region, quad, paddingPixels: 0f),
            0.1f, 0.1f, 0.3f, 0.3f);
    }

    [Test]
    public void RegionRunningOffThePictureIsNotFeatheredAtTheEdge()
    {
        // Spans the whole left edge: it must stay solid out to the picture's border, i.e. reach below 0.
        var quad = UIBox2.FromDimensions(0f, 0f, 1000f, 500f);
        var region = UIBox2.FromDimensions(-50f, 0f, 350f, 500f);

        var mapped = KsLobbyContrastMath.RegionToQuadSpace(region, quad, paddingPixels: 0f);

        Assert.That(mapped.X, Is.LessThan(0f));
        Assert.That(mapped.Z, Is.EqualTo(0.3f).Within(0.0001f));
    }

    [Test]
    public void ExtremeOverreachIsClamped()
    {
        var quad = UIBox2.FromDimensions(0f, 0f, 100f, 100f);
        var region = UIBox2.FromDimensions(-100000f, -100000f, 200000f, 200000f);

        AssertRect(KsLobbyContrastMath.RegionToQuadSpace(region, quad, paddingPixels: 0f),
            -KsLobbyContrastMath.OverreachLimit, -KsLobbyContrastMath.OverreachLimit,
            KsLobbyContrastMath.OverreachLimit, KsLobbyContrastMath.OverreachLimit);
    }

    [Test]
    public void RegionEntirelyOffThePictureIsUnused()
    {
        var quad = UIBox2.FromDimensions(0f, 0f, 1000f, 500f);

        Assert.That(KsLobbyContrastMath.RegionToQuadSpace(UIBox2.FromDimensions(1100f, 0f, 100f, 100f), quad, 0f),
            Is.EqualTo(Vector4.Zero));
        Assert.That(KsLobbyContrastMath.RegionToQuadSpace(UIBox2.FromDimensions(-300f, 0f, 100f, 100f), quad, 0f),
            Is.EqualTo(Vector4.Zero));
    }

    [Test]
    public void EmptyRegionOrQuadIsUnused()
    {
        var quad = UIBox2.FromDimensions(0f, 0f, 1000f, 500f);

        Assert.That(KsLobbyContrastMath.RegionToQuadSpace(UIBox2.FromDimensions(10f, 10f, 0f, 0f), quad, 16f),
            Is.EqualTo(Vector4.Zero));
        Assert.That(KsLobbyContrastMath.RegionToQuadSpace(UIBox2.FromDimensions(10f, 10f, 50f, 50f),
                UIBox2.FromDimensions(0f, 0f, 0f, 0f), 0f),
            Is.EqualTo(Vector4.Zero));
    }
}
