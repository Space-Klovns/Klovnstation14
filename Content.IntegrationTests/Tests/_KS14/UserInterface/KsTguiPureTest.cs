using System.Collections.Generic;
using System.Numerics;
using Content.Client._KS14.Tgui;
using Content.Client.Stylesheets.Fonts;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The theme's arithmetic, which needs no client: what gets drawn can't be seen headlessly, but its shape can be checked.
/// </summary>
[TestOf(typeof(KsTguiGeometry))]
public sealed class KsTguiPureTest
{
    private const float CornerSoftness = 0.5f;

    private static readonly Vector2[] Unused = [];

    [Test]
    public void RoundedLoopStaysOnTheBoxSides()
    {
        var box = new UIBox2(10f, 20f, 110f, 40f);
        var loop = new Vector2[KsTguiGeometry.RoundedLoopLength(segments: 3)];

        KsTguiGeometry.RoundedLoop(box, 2f, 2f, 2f, 2f, segments: 3, fringe: CornerSoftness, loop);

        foreach (var point in loop)
        {
            Assert.That(point.X, Is.InRange(box.Left - 0.01f, box.Right + 0.01f));
            Assert.That(point.Y, Is.InRange(box.Top - 0.01f, box.Bottom + 0.01f));
        }

        // Where an arc meets a side it ends exactly on the side, or the straight edges go soft too.
        Assert.That(loop[0].X, Is.EqualTo(box.Left).Within(0.001f));
    }

    [Test]
    public void RoundedLoopClampsRadiiToTheBox()
    {
        var box = new UIBox2(0f, 0f, 4f, 4f);
        var loop = new Vector2[KsTguiGeometry.RoundedLoopLength(segments: 6)];

        KsTguiGeometry.RoundedLoop(box, 100f, 100f, 100f, 100f, segments: 6, fringe: 0f, loop);

        foreach (var point in loop)
        {
            Assert.That(float.IsFinite(point.X) && float.IsFinite(point.Y));
            Assert.That(point.X, Is.InRange(-0.01f, 4.01f));
            Assert.That(point.Y, Is.InRange(-0.01f, 4.01f));
        }
    }

    [Test]
    public void SquareCornersCollapseToAPoint()
    {
        var box = new UIBox2(0f, 0f, 10f, 10f);
        var loop = new Vector2[KsTguiGeometry.RoundedLoopLength(segments: 3)];

        KsTguiGeometry.RoundedLoop(box, 0f, 0f, 0f, 0f, segments: 3, fringe: CornerSoftness, loop);

        Assert.That(loop[0], Is.EqualTo(Vector2.Zero));
        Assert.That(loop[1], Is.EqualTo(Vector2.Zero), "A fringe mustn't push a square corner out of the box");
    }

    /// <summary>
    ///     A window's first frame is smaller than its title bar. Nothing headless draws, so only this catches the assert.
    /// </summary>
    [TestCase(0f, 0f, 0f, 32f)]
    [TestCase(100f, 10f, 0f, 32f)]
    [TestCase(1f, 1f, 2f, 2f)]
    public void DeflatingPastTheBoxStaysValid(float width, float height, float side, float top)
    {
        var box = new UIBox2(5f, 5f, 5f + width, 5f + height);

        var deflated = KsTguiGeometry.Deflate(box, side, top, side, side);

        Assert.That(deflated.Width, Is.GreaterThanOrEqualTo(0f));
        Assert.That(deflated.Height, Is.GreaterThanOrEqualTo(0f));
    }

    /// <summary>
    ///     A browser blends in sRGB and the engine in linear light, so a CSS alpha has to be converted. These results are
    ///         tgui's own: a Divider over the window, a NoticeBox's dark stripe, and a Section.
    /// </summary>
    [TestCase(1f, 0.1f, "#262626", "#3C3C3C")]
    [TestCase(0f, 0.1f, "#9D7D49", "#8D7142")]
    [TestCase(0f, 0.33f, "#262626", "#191919")]
    public void TranslucentColoursLandWhereCssDoes(float overlay, float opacity, string backdropHex, string expectedHex)
    {
        var backdrop = Color.FromHex(backdropHex);
        var expected = Color.FromHex(expectedHex);

        var color = KsTguiPalettes.Translucent(new Color(overlay, overlay, overlay), opacity, backdrop);

        // What the engine's blend does with that: mix in linear light, then back to sRGB.
        var blended = Color.ToSrgb(Color.InterpolateBetween(Color.FromSrgb(backdrop), Color.FromSrgb(color), color.A));

        Assert.That(blended.R, Is.EqualTo(expected.R).Within(2f / 255f));
    }

    [Test]
    public void LogoKeepsItsAspectAndCentre()
    {
        var area = new UIBox2(0f, 32f, 400f, 600f);

        var logo = KsTguiGeometry.FitLogo(area);

        Assert.Multiple(() =>
        {
            Assert.That(logo.Width / logo.Height, Is.EqualTo(425f / 200f).Within(0.001f));

            // tgui's default 400x600 window draws it about 280 wide, at 70% of the width.
            Assert.That(logo.Width, Is.EqualTo(280f).Within(0.01f));
            Assert.That(logo.Center.X, Is.EqualTo(area.Center.X).Within(0.01f));
            Assert.That(logo.Center.Y, Is.EqualTo(area.Center.Y).Within(0.01f));
        });
    }

    [Test]
    public void LogoFitsAWideShortArea()
    {
        var area = new UIBox2(0f, 0f, 1000f, 100f);

        var logo = KsTguiGeometry.FitLogo(area);

        Assert.That(logo.Height, Is.EqualTo(70f).Within(0.01f), "The tighter axis should decide the size");
    }

    [Test]
    public void StripeOutsideTheBoxIsEmpty()
    {
        var polygon = new Vector2[6];
        var scratch = new Vector2[6];

        var count = KsTguiGeometry.ClipStripe(new Vector2(10f, 10f), 100f, 120f, polygon, scratch);

        Assert.That(count, Is.LessThan(3));
    }

    [Test]
    public void StripeIsClippedToTheBox()
    {
        var size = new Vector2(100f, 20f);
        var polygon = new Vector2[6];
        var scratch = new Vector2[6];

        var count = KsTguiGeometry.ClipStripe(size, 30f, 44f, polygon, scratch);

        Assert.That(count, Is.GreaterThanOrEqualTo(3));
        for (var i = 0; i < count; i++)
        {
            Assert.That(polygon[i].X, Is.InRange(-0.01f, size.X + 0.01f));
            Assert.That(polygon[i].Y, Is.InRange(-0.01f, size.Y + 0.01f));
            Assert.That(polygon[i].X + polygon[i].Y, Is.InRange(29.99f, 44.01f));
        }
    }

    /// <summary>
    ///     tgui's handles: south 6, east 3, a 20 corner, and none to the north or west.
    /// </summary>
    [TestCase(200f, 10f, true, KsTguiDragZone.Move)]
    [TestCase(200f, 31f, true, KsTguiDragZone.Move)]
    [TestCase(200f, 33f, true, KsTguiDragZone.None)]
    [TestCase(1f, 200f, true, KsTguiDragZone.None)]
    [TestCase(200f, 395f, true, KsTguiDragZone.Bottom)]
    [TestCase(398f, 200f, true, KsTguiDragZone.Right)]
    [TestCase(395f, 200f, true, KsTguiDragZone.None)]
    [TestCase(385f, 385f, true, KsTguiDragZone.BottomRight)]
    [TestCase(385f, 385f, false, KsTguiDragZone.None)]
    [TestCase(200f, 395f, false, KsTguiDragZone.None)]
    [TestCase(200f, 10f, false, KsTguiDragZone.Move)]
    public void WindowZones(float x, float y, bool resizable, KsTguiDragZone expected)
    {
        var zone = KsTguiWindowZones.Classify(new Vector2(x, y), new Vector2(400f, 400f), resizable);

        Assert.That(zone, Is.EqualTo(expected));
    }

    [TestCase("air alarm", "Air Alarm")]
    [TestCase("head of security", "Head of Security")]
    [TestCase("pwr game vendor", "Pwr Game Vendor")]
    [TestCase("a tale of the sea", "A Tale of the Sea")]
    [TestCase("id card console", "id Card Console")] // tgui's own quirk.
    [TestCase("Central Primary Hallway Air Alarm", "Central Primary Hallway Air Alarm")]
    [TestCase("NanoMed", "NanoMed")]
    [TestCase("пожарная тревога", "пожарная тревога")]
    [TestCase("", "")]
    public void TitlesFollowTgui(string title, string expected)
    {
        Assert.That(KsTguiText.FormatTitle(title), Is.EqualTo(expected));
    }

    [TestCase(FontKind.Regular, "Verdana")]
    [TestCase(FontKind.Bold, "Verdana-Bold")]
    [TestCase(FontKind.Italic, "Verdana-Italic")]
    [TestCase(FontKind.BoldItalic, "Verdana-BoldItalic")]
    public void FaceSelectionMatchesThePostscriptName(FontKind kind, string expected)
    {
        var faces = new List<string> { "Arial", "Verdana-Bold", "Verdana-BoldItalic", "Verdana", "Verdana-Italic", "VerdanaPro" };

        Assert.That(KsTguiFonts.SelectFace(faces, name => name, kind), Is.EqualTo(expected));
    }

    [Test]
    public void NoVerdanaMeansNoFace()
    {
        var faces = new List<string> { "Arial", "DejaVuSans" };

        Assert.That(KsTguiFonts.SelectFace(faces, name => name, FontKind.Regular), Is.Null);
    }
}
