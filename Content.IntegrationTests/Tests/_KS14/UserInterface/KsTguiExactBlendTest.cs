using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Backdrop;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Tooltips;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using XColor = Robust.Shared.Maths.Color;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The sRGB blend maths of the window dimmer and the popups, the subpixel scaling of an animating popup, the knob popup's
///         transition and the slider pointer's edge, none of which need a client. The shaders do the same sums on the GPU.
/// </summary>
[TestOf(typeof(KsTguiTint))]
public sealed class KsTguiExactBlendMathTest
{
    private static XColor Grey(float eightBit)
    {
        var value = eightBit / 255f;
        return new XColor(value, value, value, 1f);
    }

    /// <summary>
    ///     Measured in tg: <c>Window__dimmer</c> turns a pixel of 16 into about 31 and one of 26 into about 38.
    /// </summary>
    [TestCase(16f, 31f)]
    [TestCase(26f, 38f)]
    public void TheWindowDimmerBlendsInSrgbAsTgDoes(float before, float after)
    {
        var blended = KsTguiThemePalette.Nanotrasen.WindowDimmerTint.BlendOver(Grey(before));

        Assert.That(blended.R * 255f, Is.EqualTo(after).Within(1.5f));
        Assert.That(blended.G * 255f, Is.EqualTo(after).Within(1.5f));
        Assert.That(KsTguiThemePalette.Nanotrasen.WindowDimmerTint.Opacity, Is.EqualTo(0.25f));
    }

    [Test]
    public void ThePlainAlphaCouldNotGetBothDimmerResults()
    {
        // The reason the dimmer composites in its shader: one alpha, exact over the window's base, is wrong over darker content.
        var palette = KsTguiThemePalette.Nanotrasen;
        var flat = palette.WindowDimmer;

        Assert.That(Over(flat, palette.Base), Is.EqualTo(palette.WindowDimmerTint.BlendOver(palette.Base).G).Within(1.5f / 255f),
            "Exact over the backdrop it was worked out for.");

        var low = Over(flat, Grey(16f)) * 255f;
        var high = Over(flat, Grey(26f)) * 255f;
        Assert.That(Math.Abs(low - 31f) + Math.Abs(high - 38f), Is.GreaterThan(3f), "And off over the darker content.");
    }

    private static float Over(XColor translucent, XColor backdrop)
    {
        // This engine's blend: linear light.
        var top = XColor.FromSrgb(translucent);
        var under = XColor.FromSrgb(backdrop);
        var mixed = new XColor(
            top.R * translucent.A + under.R * (1f - translucent.A),
            top.G * translucent.A + under.G * (1f - translucent.A),
            top.B * translucent.A + under.B * (1f - translucent.A));
        return XColor.ToSrgb(mixed).G;
    }

    [Test]
    public void TooltipAndMenuAreTheirCssColourAtTheirCssOpacityOverTheBackdrop()
    {
        var palette = KsTguiThemePalette.Nanotrasen;
        var backdrop = new XColor(0.2f, 0.4f, 0.6f, 1f);

        foreach (var tint in new[] { palette.TooltipTint, palette.MenuTint })
        {
            Assert.That(tint.Opacity, Is.EqualTo(0.85f), "--tooltip-background and --dropdown-menu-background are at .85.");

            var blended = tint.BlendOver(backdrop);
            Assert.That(blended.R, Is.EqualTo(tint.Color.R * 0.85f + 0.2f * 0.15f).Within(1e-5f));
            Assert.That(blended.G, Is.EqualTo(tint.Color.G * 0.85f + 0.4f * 0.15f).Within(1e-5f));
            Assert.That(blended.B, Is.EqualTo(tint.Color.B * 0.85f + 0.6f * 0.15f).Within(1e-5f));
            Assert.That(blended.A, Is.EqualTo(1f), "Opaque: the popup is the result.");
        }

        Assert.That(KsTguiThemePalette.Wizard.TooltipTint.Opacity, Is.EqualTo(1f), "Wizard's tooltip is an opaque colour.");
        Assert.That(palette.MenuTint.Color, Is.Not.EqualTo(palette.TooltipTint.Color));
    }

    [Test]
    public void TheFlatFallbackStaysTheApproximationOfTheTint()
    {
        var palette = KsTguiThemePalette.Nanotrasen;

        Assert.That(palette.Menu, Is.EqualTo(palette.MenuTint.Approximate(palette.Base)));
        Assert.That(palette.Tooltip, Is.EqualTo(palette.TooltipTint.Approximate(palette.Base)));
        Assert.That(palette.WindowDimmer, Is.EqualTo(palette.WindowDimmerTint.Approximate(palette.Base)));
    }

    [TestCase(1f, 1f, false)]
    [TestCase(0.9f, 0f, true)]
    [TestCase(0.95f, 0.5f, true)]
    [TestCase(1f, 0.5f, true)]
    [TestCase(0.9f, 1f, true)]
    [TestCase(0.99999f, 0.99999f, false)]
    public void APopupIsDrawnThroughTheTextureOnlyWhileItAnimates(float scale, float alpha, bool expected)
    {
        Assert.That(KsTguiScaledRaster.ShouldRasterize(scale, alpha), Is.EqualTo(expected));
    }

    [Test]
    public void ThePivotMathsGivesSubpixelOffsetsAndKeepsThePivotStill()
    {
        var size = new Vector2(101f, 33f);
        var topLeft = new Vector2(200f, 300f);
        var pivot = new Vector2(0.5f, 0f);

        foreach (var scale in new[] { 0.9f, 0.93f, 0.97f, 0.999f })
        {
            var box = KsTguiScaledRaster.ScaledBox(topLeft, size, pivot, scale);

            Assert.That(box.Width, Is.EqualTo(size.X * scale).Within(1e-3f));
            Assert.That(box.Left + box.Width / 2f, Is.EqualTo(topLeft.X + size.X / 2f).Within(1e-3f), "The pivot stays put.");
            Assert.That(box.Top, Is.EqualTo(topLeft.Y).Within(1e-3f), "The pivot edge stays put.");
        }

        var shift = KsTguiScaledRaster.PivotShift(size, pivot, 0.93f);
        Assert.That(shift.X, Is.EqualTo(3.535f).Within(1e-3f));
        Assert.That(shift.X, Is.Not.EqualTo(MathF.Round(shift.X)), "Not rounded to a pixel.");

        var bottom = KsTguiScaledRaster.ScaledBox(topLeft, size, new Vector2(0.5f, 1f), 0.9f);
        Assert.That(bottom.Bottom, Is.EqualTo(topLeft.Y + size.Y).Within(1e-3f), "A popup above its control scales about its bottom edge.");
        Assert.That(KsTguiScaledRaster.ScaledBox(topLeft, size, pivot, 1f), Is.EqualTo(UIBox2.FromDimensions(topLeft, size)));
    }

    [Test]
    public void TheFloatingTransitionOpensClosesAndTurnsRoundFromWhereItGot()
    {
        var transition = new KsTguiFloatingTransition();
        Assert.That(transition.Value, Is.EqualTo(0f));
        Assert.That(transition.Scale, Is.EqualTo(0.9f));
        Assert.That(transition.Settled);

        transition.SetOpen(true);
        transition.Advance(0.1f);
        var eased = KsTguiFeelUIController.CssEase(0.5f);
        Assert.That(transition.Alpha, Is.EqualTo(eased).Within(1e-4f));
        Assert.That(transition.Scale, Is.EqualTo(0.9f + 0.1f * eased).Within(1e-4f));

        // Closed half way: starts from there, not from 1, and takes the whole 0.2 s again.
        var reached = transition.Alpha;
        transition.SetOpen(false);
        transition.Advance(0.0001f);
        Assert.That(transition.Alpha, Is.EqualTo(reached).Within(0.01f));
        transition.Advance(0.1f);
        Assert.That(transition.Alpha, Is.EqualTo(reached * (1f - eased)).Within(0.02f));
        Assert.That(transition.Settled, Is.False);
        transition.Advance(0.2f);
        Assert.That(transition.Alpha, Is.EqualTo(0f));
        Assert.That(transition.Settled);
        Assert.That(transition.Open, Is.False);

        // Reopened mid-close: turns round from the current value.
        transition.SetOpen(true);
        transition.Advance(0.05f);
        transition.SetOpen(false);
        transition.Advance(0.05f);
        var closing = transition.Alpha;
        transition.SetOpen(true);
        Assert.That(transition.Open);
        transition.Advance(0.0001f);
        Assert.That(transition.Alpha, Is.EqualTo(closing).Within(0.01f), "No jump.");
        transition.Advance(0.3f);
        Assert.That(transition.Alpha, Is.EqualTo(1f));
    }

    [Test]
    public void TheSliderPointerHasAOnePixelAlphaFringe()
    {
        var vertices = new DrawVertexUV2DColor[KsTguiSlider.PointerVertexCount];
        var apex = new Vector2(50f, 20f);
        const float halfBase = 8f;
        const float baseY = 32f;
        KsTguiSlider.WritePointer(vertices, apex, halfBase, baseY, XColor.White);

        // The solid triangle first: inset from the true outline, full alpha.
        for (var i = 0; i < 3; i++)
            Assert.That(vertices[i].Color.A, Is.EqualTo(1f));

        Assert.That(vertices[0].Position.Y, Is.GreaterThan(apex.Y), "The apex is pulled in.");
        Assert.That(vertices[1].Position.Y, Is.LessThan(baseY), "And the base edge.");

        // The fringe: soft vertices with no alpha, outside the true outline, half a pixel either side of it.
        var soft = vertices.Skip(3).Where(vertex => vertex.Color.A == 0f).ToArray();
        var solid = vertices.Skip(3).Where(vertex => vertex.Color.A == 1f).ToArray();
        Assert.That(soft, Has.Length.EqualTo(9));
        Assert.That(solid, Has.Length.EqualTo(9));
        Assert.That(soft.Min(vertex => vertex.Position.Y), Is.LessThan(apex.Y), "The apex's fringe is above the true apex.");
        Assert.That(soft.Max(vertex => vertex.Position.Y), Is.GreaterThan(baseY));

        var baseSoft = soft.Max(vertex => vertex.Position.Y);
        var baseSolid = solid.Max(vertex => vertex.Position.Y);
        Assert.That(baseSoft - baseSolid, Is.EqualTo(1f).Within(1e-3f), "The ramp across the base edge is one pixel.");
        Assert.That(baseSoft - baseY, Is.EqualTo(0.5f).Within(1e-3f), "Centred on the true edge.");
        Assert.That(baseY - baseSolid, Is.EqualTo(0.5f).Within(1e-3f));
    }
}

/// <summary>
///     The same on a client: the colours each popup is given from the palette, the popup background that is left out only
///         while the backdrop pass paints it, the dimmer's place in its window and the knob popup's transition.
/// </summary>
public sealed class KsTguiExactBlendTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private IUserInterfaceManager UiManager => Client.ResolveDependency<IUserInterfaceManager>();

    private KsTguiFeelUIController Feel => UiManager.GetUIController<KsTguiFeelUIController>();

    private async Task SetTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);
    }

    private static void Restyle(Control control)
    {
        control.ForceRunStyleUpdate();
        foreach (var child in control.Children)
            Restyle(child);
    }

    [Test]
    public async Task EachPopupIsGivenItsCssColourAndOpacityFromThePalette()
    {
        await SetTheme(true);
        var palette = KsTguiThemePalette.Nanotrasen;

        await Client.WaitPost(() =>
        {
            // Tooltip.
            var window = new DefaultWindow { SetSize = new Vector2(400f, 300f) };
            var button = new Button { Text = "button", ToolTip = "a tooltip" };
            window.Contents.AddChild(button);
            window.OpenCentered();
            var tooltip = new Tooltip { Text = "a tooltip", Tracking = true };
            UiManager.PopupRoot.AddChild(tooltip);
            Feel.TakeTooltip(tooltip, button);
            Feel.UpdateWindowTooltips(0f);
            var host = (KsTguiTooltipHost)tooltip.Parent!;
            Restyle(host);
            Assert.That(host.Background.FindTint(host), Is.EqualTo(palette.TooltipTint));

            // Dropdown menu: the panel the feel controller finds in a menu.
            var scaleHost = new KsTguiScaleHost();
            var panel = new PanelContainer();
            panel.AddStyleClass(OptionButton.StyleClassOptionsBackground);
            scaleHost.AddChild(panel);
            UiManager.PopupRoot.AddChild(scaleHost);
            Restyle(scaleHost);
            Assert.That(scaleHost.Background.FindTint(scaleHost), Is.EqualTo(palette.MenuTint));

            // Knob and slider popups.
            var knob = new KsTguiKnob();
            var slider = new KsTguiSlider();
            UiManager.PopupRoot.AddChild(knob);
            UiManager.PopupRoot.AddChild(slider);
            Restyle(knob);
            Restyle(slider);
            Assert.That(((KsTguiStyleBox)knob.PopupPanel.PanelOverride!).Tint, Is.EqualTo(palette.TooltipTint));
            Assert.That(((KsTguiStyleBox)((KsTguiBackdropPanel)slider.PopupControl).PanelOverride!).Tint, Is.EqualTo(palette.TooltipTint));

            knob.Orphan();
            slider.Orphan();
            scaleHost.Orphan();
            window.Close();
        });
    }

    [Test]
    public async Task APanelsBackgroundIsLeftOutOnlyWhileItIsComposited()
    {
        await SetTheme(true);

        await Client.WaitPost(() =>
        {
            var original = new KsTguiStyleBox(new XColor(0.1f, 0.1f, 0.1f, 0.8f), 4f)
            {
                Tint = new KsTguiTint(new XColor(0.1f, 0.1f, 0.1f, 1f), 0.85f),
                BorderColor = XColor.White,
                BorderThickness = new Thickness(1f),
            };
            original.SetContentMarginOverride(StyleBox.Margin.Horizontal, 9f);
            var panel = new PanelContainer { PanelOverride = original };
            var holder = new Control();
            holder.AddChild(panel);

            var background = new KsTguiBackdropBackground();
            Assert.That(background.FindTint(holder), Is.EqualTo(original.Tint));
            Assert.That(panel.PanelOverride, Is.SameAs(original), "Finding it changes nothing.");
            Assert.That(background.Hollowed, Is.False);

            background.Apply(composited: true);
            var hollow = (KsTguiStyleBox)panel.PanelOverride!;
            Assert.That(hollow, Is.Not.SameAs(original));
            Assert.That(hollow.BackgroundColor.A, Is.EqualTo(0f), "No background while the pass paints it.");
            Assert.That(hollow.BorderColor, Is.EqualTo(XColor.White), "The border is still drawn.");
            Assert.That(hollow.BorderThickness, Is.EqualTo(original.BorderThickness));
            Assert.That(hollow.RadiusTopLeft, Is.EqualTo(4f));
            Assert.That(hollow.GetContentMargin(StyleBox.Margin.Left), Is.EqualTo(9f), "Same size, so the layout is the same.");
            Assert.That(background.Hollowed);
            Assert.That(background.FindTint(holder), Is.EqualTo(original.Tint), "Still found while hollow.");

            // The pass didn't run this frame (blur off, or inside another pass): the panel draws as it always did.
            background.Apply(composited: false);
            Assert.That(panel.PanelOverride, Is.SameAs(original));
            Assert.That(background.Hollowed, Is.False);

            background.Apply(composited: true);
            background.Release();
            Assert.That(panel.PanelOverride, Is.SameAs(original), "Let go of, it is put back.");

            // A box with no tint is never hollowed.
            panel.PanelOverride = new KsTguiStyleBox(XColor.White);
            var untinted = new KsTguiBackdropBackground();
            Assert.That(untinted.FindTint(holder), Is.Null);
            untinted.Apply(composited: true);
            Assert.That(untinted.Hollowed, Is.False);
        });
    }

    [Test]
    public async Task TheDimmerIsDrawnAfterTheWindowsBackgroundAndContentsAndTheWindowDrawsNothingItself()
    {
        await SetTheme(true);

        await Client.WaitPost(() =>
        {
            var windows = new BaseWindow[]
            {
                new DefaultWindow { SetSize = new Vector2(300f, 200f) },
                new Content.Client.UserInterface.Controls.FancyWindow { SetSize = new Vector2(300f, 200f) },
            };

            foreach (var window in windows)
            {
                // The window's own background is a child panel, not drawn by the window: nothing is missed by walking the
                //      siblings before the dimmer. If a window type drew itself, its picture would be missing from the capture.
                var draw = window.GetType().GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic,
                    new[] { typeof(DrawingHandleScreen) });
                Assert.That(draw!.DeclaringType, Is.EqualTo(typeof(Control)), $"{window.GetType().Name} draws nothing itself.");
                var drawRender = window.GetType().GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic,
                    new[] { typeof(IRenderHandle) });
                Assert.That(drawRender!.DeclaringType, Is.EqualTo(typeof(Control)));

                var dimmer = new KsTguiWindowDimmer { Visible = true };
                window.AddChild(dimmer);
                window.OpenCentered();

                var earlier = new System.Collections.Generic.List<(Control Control, Vector2i Position)>();
                KsTguiBackdropMath.EarlierControls(dimmer, dimmer.GlobalPixelPosition, earlier);

                var background = window.Children.First();
                Assert.That(earlier.Select(entry => entry.Control), Does.Contain(background), "The background panel is captured.");
                foreach (var child in window.Children.Where(child => child != dimmer))
                    Assert.That(earlier.Select(entry => entry.Control), Does.Contain(child), "So are the window's contents.");

                Assert.That(dimmer.GetPositionInParent(), Is.EqualTo(window.ChildCount - 1), "Last: it is drawn over them.");
                Assert.That(dimmer.MouseFilter, Is.EqualTo(Control.MouseFilterMode.Ignore));
                window.Close();
            }
        });
    }

    [Test]
    public async Task TheKnobPopupOpensAndClosesWithFloatingsTransition()
    {
        await SetTheme(true);

        await Client.WaitPost(() =>
        {
            var knob = new KsTguiKnob { KnobSize = 1.25f, MinValue = 0f, MaxValue = 1000f, Step = 5f, StepPixelSize = 1f };
            UiManager.PopupRoot.AddChild(knob);
            knob.SetValue(500f);
            var host = (KsTguiScaleHost)knob.PopupControl;

            Assert.That(host.Parent, Is.Null, "Nothing shown before the value moves.");

            knob.BeginPress(300f);
            knob.Advance(KsTguiKnob.HoldSeconds);
            knob.DragTo(290f);
            Assert.That(knob.PopupVisible);

            knob.UpdatePopup(0.0f);
            Assert.That(host.Parent, Is.SameAs(UiManager.PopupRoot));
            Assert.That(knob.PopupTransition.Open);
            Assert.That(host.Modulate.A, Is.EqualTo(0f), "Starts invisible and at scale .9, as Floating's initial state.");
            Assert.That(host.Scale, Is.EqualTo(0.9f).Within(1e-4f));

            knob.UpdatePopup(0.1f);
            var eased = KsTguiFeelUIController.CssEase(0.5f);
            Assert.That(host.Modulate.A, Is.EqualTo(eased).Within(0.01f));
            Assert.That(host.Scale, Is.EqualTo(0.9f + 0.1f * eased).Within(0.01f));

            knob.UpdatePopup(0.2f);
            Assert.That(host.Modulate.A, Is.EqualTo(1f));
            Assert.That(host.Scale, Is.EqualTo(1f));

            // Let go: it fades and shrinks for 0.2 s, still in the popup layer, and is then gone.
            knob.Release();
            Assert.That(knob.PopupVisible, Is.False);
            Assert.That(host.Parent, Is.SameAs(UiManager.PopupRoot), "Not removed at once.");
            knob.UpdatePopup(0.05f);
            var closing = host.Modulate.A;
            Assert.That(closing, Is.LessThan(1f));
            Assert.That(closing, Is.GreaterThan(0f));
            Assert.That(host.Parent, Is.SameAs(UiManager.PopupRoot));

            // Opened again before it finished: turns round from where it got to, with no jump to hidden or shown.
            knob.BeginPress(300f);
            knob.Advance(KsTguiKnob.HoldSeconds);
            knob.DragTo(280f);
            knob.UpdatePopup(0.0001f);
            Assert.That(knob.PopupTransition.Open);
            Assert.That(host.Modulate.A, Is.EqualTo(closing).Within(0.02f));
            knob.UpdatePopup(0.3f);
            Assert.That(host.Modulate.A, Is.EqualTo(1f));

            knob.Release();
            knob.UpdatePopup(0.3f);
            Assert.That(host.Parent, Is.Null, "Finished closing, it is taken out of the popup layer.");
            Assert.That(knob.PopupTransition.Value, Is.EqualTo(0f));
            Assert.That(knob.PopupTransition.Open, Is.False);

            knob.Orphan();
        });
    }

    [Test]
    public async Task ATooltipHostStaysOnItsFinalPixelThroughoutItsAnimation()
    {
        await SetTheme(true);

        DefaultWindow window = null!;
        Button button = null!;
        await Client.WaitPost(() =>
        {
            window = new DefaultWindow { SetSize = new Vector2(400f, 300f) };
            button = new Button { Text = "button", ToolTip = "a tooltip" };
            window.Contents.AddChild(button);
            window.OpenCentered();
        });
        await Pair.RunTicksSync(3);

        KsTguiTooltipHost host = null!;
        await Client.WaitPost(() =>
        {
            var tooltip = new Tooltip { Text = "a tooltip", Tracking = true };
            UiManager.PopupRoot.AddChild(tooltip);
            Feel.TakeTooltip(tooltip, button);
            Feel.UpdateWindowTooltips(0f);
            host = (KsTguiTooltipHost)tooltip.Parent!;
            Assert.That(host.Scale, Is.LessThan(1f));
            Assert.That(KsTguiScaledRaster.ShouldRasterize(host.Scale, host.Modulate.A), "Animating: drawn through the texture.");
            Assert.That(host.Pivot.X, Is.EqualTo(0.5f).Within(0.01f), "Opening below: scales about the top edge's middle.");
            Assert.That(host.Pivot.Y, Is.EqualTo(0f).Or.EqualTo(1f).Within(0.01f), "Top edge opening down, bottom edge opening up.");
        });
        await Pair.RunTicksSync(2);

        Vector2i start = default;
        var placed = Vector2.Zero;
        await Client.WaitPost(() =>
        {
            start = host.PixelPosition;
            placed = host.Position;
        });

        // No ticks between: the controller's own frames would carry the animation on.
        await Client.WaitPost(() =>
        {
            Feel.UpdateWindowTooltips(0.05f);
            Assert.That(host.Scale, Is.InRange(0.9f, 0.999f));
            Assert.That(host.Position, Is.EqualTo(placed), "Not moved by the scale: that stepped by whole pixels.");
        });

        await Client.WaitPost(() => Feel.UpdateWindowTooltips(0.3f));
        await Pair.RunTicksSync(2);
        await Client.WaitAssertion(() =>
        {
            Assert.That(host.Scale, Is.EqualTo(1f));
            Assert.That(KsTguiScaledRaster.ShouldRasterize(host.Scale, host.Modulate.A), Is.False, "Settled: drawn as any control.");
            Assert.That(host.PixelPosition, Is.EqualTo(start), "Where it was throughout.");
            window.Close();
        });
    }
}
