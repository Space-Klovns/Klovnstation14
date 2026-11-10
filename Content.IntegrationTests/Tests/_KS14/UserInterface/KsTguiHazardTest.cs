using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._FarHorizons.Power.UI;
using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     tgui's yellow and black hazard stripes (the anomalous weather tower's <c>.AnomalyTower__HazardBg</c>), and the one
///         place KS14 uses them: behind the nuclear reactor's silence button. Nothing draws headlessly, so the stripes are
///         checked as the triangles the style box would draw.
/// </summary>
[TestOf(typeof(KsTguiHazardStyleBox))]
public sealed class KsTguiHazardTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private IUserInterfaceManager UiManager => Client.ResolveDependency<IUserInterfaceManager>();

    private async Task SetTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);
    }

    /// <summary>
    ///     <c>repeating-linear-gradient(45deg, #ffc107 0 15px, #000 15px 30px)</c> in tiles from the top left: a 45deg
    ///         gradient starts at each tile's bottom left corner, so a stripe edge runs through the top left corner, with yellow
    ///         to its right and black below it.
    /// </summary>
    [Test]
    public void ThePatternIsTheCssGradient()
    {
        var diagonal = MathF.Sqrt(2f);

        Assert.Multiple(() =>
        {
            Assert.That(KsTguiHazardStyleBox.IsBlack(new Vector2(1f, 0f)), Is.False, "Right of the corner: the start of a yellow band");
            Assert.That(KsTguiHazardStyleBox.IsBlack(new Vector2(0f, 1f)), Is.True, "Below it: the end of a black band");
            Assert.That(KsTguiHazardStyleBox.IsBlack(new Vector2(15f * diagonal + 1f, 0f)), Is.True, "15px across the stripes, black");
            Assert.That(KsTguiHazardStyleBox.IsBlack(new Vector2(30f * diagonal + 1f, 0f)), Is.False, "and 30px, yellow again");
            Assert.That(KsTguiHazardStyleBox.IsBlack(new Vector2(40f, 40f)), Is.False, "Stripes run from top left to bottom right");
            Assert.That(KsTguiHazardStyleBox.IsBlack(new Vector2(0f, 2f), uiScale: 2f), Is.True, "and scale with the UI");
            Assert.That(KsTguiHazardStyleBox.IsBlack(new Vector2(2f * (15f * diagonal + 1f), 0f), uiScale: 2f), Is.True);
        });
    }

    /// <summary>
    ///     Every point of a box is inside a drawn stripe exactly when the pattern says it is black.
    /// </summary>
    [Test]
    public void TheDrawnStripesAreThePattern()
    {
        var box = new KsTguiHazardStyleBox();

        foreach (var (size, scale) in new[] { (new Vector2(137f, 53f), 1f), (new Vector2(53f, 137f), 1f), (new Vector2(211f, 40f), 1.5f) })
        {
            var inner = UIBox2.FromDimensions(new Vector2(17f, 9f), size);
            var count = box.StripeTriangles(inner, scale, Color.Black, out var vertices);
            var triangles = vertices.Take(count).ToArray();
            Assert.That(triangles.Length % 3, Is.EqualTo(0));

            var mismatches = new List<string>();
            for (var y = 0.5f; y < size.Y; y += 1f)
            {
                for (var x = 0.5f; x < size.X; x += 1f)
                {
                    var local = new Vector2(x, y);

                    // Too close to an edge to call.
                    var along = (x - y) / MathF.Sqrt(2f) / scale;
                    var toEdge = MathF.Abs(along / 15f - MathF.Round(along / 15f)) * 15f;
                    if (toEdge < 0.25f)
                        continue;

                    var drawn = InsideAny(triangles, inner.TopLeft + local);
                    if (drawn != KsTguiHazardStyleBox.IsBlack(local, scale))
                        mismatches.Add($"{local} drawn={drawn}");
                }
            }

            Assert.That(mismatches, Is.Empty, $"size {size} at {scale}: {string.Join(", ", mismatches.Take(10))}");
        }
    }

    private static bool InsideAny(DrawVertexUV2DColor[] triangles, Vector2 point)
    {
        for (var i = 0; i < triangles.Length; i += 3)
        {
            if (Inside(triangles[i].Position, triangles[i + 1].Position, triangles[i + 2].Position, point))
                return true;
        }

        return false;
    }

    private static bool Inside(Vector2 a, Vector2 b, Vector2 c, Vector2 point)
    {
        static float Cross(Vector2 from, Vector2 to, Vector2 p) => (to.X - from.X) * (p.Y - from.Y) - (to.Y - from.Y) * (p.X - from.X);

        var d1 = Cross(a, b, point);
        var d2 = Cross(b, c, point);
        var d3 = Cross(c, a, point);
        var negative = d1 < 0f || d2 < 0f || d3 < 0f;
        var positive = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(negative && positive);
    }

    /// <summary>
    ///     The reactor's silence button sits in the stripes under tgui, as tgui's red button with its contents 6px inside the
    ///         3px border, and is Baron's tinted button with no panel under Baron. Switching theme switches it.
    /// </summary>
    [Test]
    public async Task TheReactorsSilenceButtonFollowsTheTheme()
    {
        await SetTheme(true);

        NuclearReactorWindow window = null;
        KsTguiHazardPanel panel = null;
        Button silence = null;

        await Client.WaitPost(() =>
        {
            window = new NuclearReactorWindow();
            window.OpenCentered();
            panel = Find<KsTguiHazardPanel>(window).Single();
            silence = (Button)panel.Children.Single();
        });

        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            Assert.That(panel.Shown, Is.True);
            Assert.That(panel.Box, Is.TypeOf<KsTguiHazardStyleBox>());
            Assert.That(silence.Position, Is.EqualTo(new Vector2(KsTguiHazardStyleBox.BorderWidth + 6f)), "m={1} inside the border");
            Assert.That(silence.Modulate, Is.EqualTo(Color.White), "Baron's tint is off, so tgui's red shows as itself");
            Assert.That(silence.HasStyleClass(KsTguiColors.ButtonClass(KsTguiColor.Red)), Is.True);
        });

        await SetTheme(false);

        await Client.WaitAssertion(() =>
        {
            Assert.That(panel.Shown, Is.False);
            Assert.That(panel.Box, Is.Null, "Baron draws nothing");
            Assert.That(panel.TryGetStyleProperty<StyleBox>(PanelContainer.StylePropertyPanel, out _), Is.False, "not even its dark panel, which every bare PanelContainer gets");
            Assert.That(silence.Position, Is.EqualTo(Vector2.Zero), "and the button fills the panel, as it did before it was in one");
            Assert.That(silence.Size, Is.EqualTo(panel.Size));
            Assert.That(silence.Modulate, Is.EqualTo(NuclearReactorWindow.KsSilenceBaronModulate), "Baron keeps its tint");
        });

        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            Assert.That(silence.Modulate, Is.EqualTo(Color.White), "and back");
            window.Close();
        });
    }

    private static IEnumerable<T> Find<T>(Control control) where T : Control
    {
        if (control is T found)
            yield return found;

        foreach (var child in control.Children)
        {
            foreach (var inner in Find<T>(child))
                yield return inner;
        }
    }
}
