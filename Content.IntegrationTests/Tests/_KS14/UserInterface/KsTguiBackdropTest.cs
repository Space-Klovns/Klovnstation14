using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Backdrop;
using Content.Client._KS14.Tgui.Tooltips;
using Content.Client.Viewport;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The numbers and rules behind tgui's backdrop blur, which need no client. Nothing headless draws, so the drawing is
///         for a real client to show.
/// </summary>
[TestOf(typeof(KsTguiBackdropMath))]
public sealed class KsTguiBackdropMathTest
{
    [Test]
    public void TheWeightsSumToOne()
    {
        var weights = KsTguiBackdropMath.Weights();

        Assert.That(weights, Has.Length.EqualTo(KsTguiBackdropMath.TapRadius * 2 + 1));
        Assert.That(weights.Sum(), Is.EqualTo(1f).Within(1e-5f));
        Assert.That(weights[KsTguiBackdropMath.TapRadius], Is.EqualTo(weights.Max()), "The middle tap weighs most.");
        Assert.That(weights[0], Is.EqualTo(weights[^1]).Within(1e-7f), "Symmetric.");
    }

    [Test]
    public void TheBlurIsCssTwelvePixelsStandardDeviation()
    {
        Assert.That(KsTguiBackdropMath.SigmaPixels, Is.EqualTo(12f), "blur(12px)");
        Assert.That(KsTguiBackdropMath.SigmaTaps * KsTguiBackdropMath.TapStep, Is.EqualTo(12f));

        // The taps stop at three sigma, which takes a little off the spread.
        Assert.That(KsTguiBackdropMath.EffectiveSigmaPixels(), Is.EqualTo(12f).Within(0.5f));
        Assert.That(KsTguiBackdropMath.TapRadius * KsTguiBackdropMath.TapStep, Is.EqualTo(3f * 12f), "Three sigma either side.");
    }

    [TestCase(0.25f, 0.25f)]
    [TestCase(1f, 1f)]
    [TestCase(-0.25f, 0.25f)]
    [TestCase(1.25f, 0.75f)]
    [TestCase(2.25f, 0.25f)]
    [TestCase(-1.25f, 0.75f)]
    [TestCase(-4.5f, 0.5f)]
    public void EdgesMirrorAsATriangleWave(float coordinate, float expected)
    {
        Assert.That(KsTguiBackdropMath.Mirror(coordinate), Is.EqualTo(expected).Within(1e-5f));
    }

    [Test]
    public void TheRegionIsThePanelsSizeRoundedToWholePixels()
    {
        var region = KsTguiBackdropMath.Region(new Vector2(10.4f, 20.6f), new Vector2i(100, 30));

        Assert.That(region, Is.EqualTo(UIBox2i.FromDimensions(new Vector2i(10, 21), new Vector2i(100, 30))));
    }

    [TestCase(0, 10)]
    [TestCase(10, 0)]
    [TestCase(-1, 10)]
    [TestCase(KsTguiBackdropMath.MaxSide + 1, 10)]
    [TestCase(10, KsTguiBackdropMath.MaxSide + 1)]
    public void NoPanelOfThatSizeIsBlurred(int width, int height)
    {
        Assert.That(KsTguiBackdropMath.Region(Vector2.Zero, new Vector2i(width, height)), Is.Null);
    }
}

/// <summary>
///     The backdrop blur on a client: when a pass is on, the viewport hearing of it, and the dropdown menu's closing
///         transition.
/// </summary>
[TestOf(typeof(KsTguiBackdropUIController))]
public sealed class KsTguiBackdropTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private IUserInterfaceManager UiManager => Client.ResolveDependency<IUserInterfaceManager>();

    private KsTguiFeelUIController Feel => UiManager.GetUIController<KsTguiFeelUIController>();

    private KsTguiBackdropUIController Backdrop => UiManager.GetUIController<KsTguiBackdropUIController>();

    private async Task SetTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);
    }

    private static void Press(BaseButton button)
    {
        var field = typeof(BaseButton).GetField(nameof(BaseButton.OnPressed), BindingFlags.Instance | BindingFlags.NonPublic);
        var handler = (Action<BaseButton.ButtonEventArgs>)field.GetValue(button);
        handler?.Invoke(new BaseButton.ButtonEventArgs(button, null!));
    }

    private static void Restyle(Control control)
    {
        control.ForceRunStyleUpdate();
        foreach (var child in control.Children)
            Restyle(child);
    }

    /// <summary>
    ///     A dropdown, opened. The popup comes out for the test to close.
    /// </summary>
    private (OptionButton Option, Popup Popup, KsTguiScaleHost Host) OpenDropdown(OptionButton existing = null)
    {
        var option = existing ?? new OptionButton();
        if (existing == null)
        {
            option.AddItem("one");
            option.AddItem("two");
            UiManager.PopupRoot.AddChild(option);
            Restyle(option);
            option.Measure(new Vector2(300f, 300f));
            option.Arrange(UIBox2.FromDimensions(new Vector2(20f, 20f), option.DesiredSize));
        }

        UiManager.SetHovered(option);
        Press(option);

        var popup = UiManager.ModalRoot.Children.OfType<Popup>().Single(found => found.HasStyleClass(OptionButton.StyleClassPopup));
        var host = (KsTguiScaleHost)popup.Children[0];
        return (option, popup, host);
    }

    private void FinishOpening()
    {
        Feel.FrameUpdate(new FrameEventArgs(0.01f));
        Feel.FrameUpdate(new FrameEventArgs(0.3f));
    }

    /// <summary>
    ///     The backdrop of a control is what the tree draws before it: earlier siblings, then each ancestor's earlier siblings.
    /// </summary>
    [Test]
    public async Task TheBackdropIsEverythingDrawnBeforeTheAnchor()
    {
        await Client.WaitAssertion(() =>
        {
            var root = new Control { Name = "root" };
            var windows = new Control { Name = "windows" };
            var hud = new Control { Name = "hud" };
            var popups = new Control { Name = "popups" };
            var later = new Control { Name = "later" };
            var earlierPopup = new Control { Name = "earlierPopup" };
            var anchor = new Control { Name = "anchor" };
            var laterPopup = new Control { Name = "laterPopup" };
            var inner = new Control { Name = "inner" };

            root.AddChild(windows);
            root.AddChild(hud);
            root.AddChild(popups);
            root.AddChild(later);
            popups.AddChild(earlierPopup);
            popups.AddChild(anchor);
            popups.AddChild(laterPopup);
            anchor.AddChild(inner);

            Place(windows, 0, 0);
            Place(hud, 5, 5);
            Place(popups, 10, 20);
            Place(later, 1, 1);
            Place(earlierPopup, 5, 5);
            Place(anchor, 30, 40);

            var result = new List<(Control Control, Vector2i Position)>();
            KsTguiBackdropMath.EarlierControls(anchor, new Vector2i(40, 60), result);

            Assert.That(result.Select(entry => entry.Control), Is.EqualTo(new[] { windows, hud, earlierPopup }),
                "In drawing order, with nothing from the anchor or after it.");
            Assert.That(result[1].Position, Is.EqualTo(new Vector2i(5, 5)));
            Assert.That(result[2].Position, Is.EqualTo(new Vector2i(15, 25)), "The popup layer at 10,20 and the popup 5,5 in it.");
        });
    }

    [Test]
    public async Task AnAnchorWithNothingBeforeItHasNoBackdrop()
    {
        await Client.WaitAssertion(() =>
        {
            var root = new Control();
            var anchor = new Control();
            root.AddChild(anchor);
            root.AddChild(new Control());

            var result = new List<(Control Control, Vector2i Position)>();
            KsTguiBackdropMath.EarlierControls(anchor, Vector2i.Zero, result);

            Assert.That(result, Is.Empty);
        });
    }

    private static void Place(Control control, float x, float y)
    {
        control.Measure(new Vector2(50f, 50f));
        control.Arrange(UIBox2.FromDimensions(new Vector2(x, y), new Vector2(20f, 20f)));
    }

    [Test]
    public async Task ThePassFlagIsSetOnlyWhileThePassRunsAndHeardByTheViewport()
    {
        await SetTheme(true);

        await Client.WaitPost(() =>
        {
            var viewport = new ScalingViewport();
            Assert.That(Backdrop.PassActive, Is.False);
            Assert.That(viewport.KsBackdropPassActive, Is.False);

            Backdrop.RunPass(() =>
            {
                Assert.That(Backdrop.PassActive, Is.True);
                Assert.That(viewport.KsBackdropPassActive, Is.True, "The viewport draws only its last picture then.");
                Assert.That(Backdrop.CanBlur, Is.False, "A popup drawn in the pass doesn't start another.");
                Assert.Throws<InvalidOperationException>(() => Backdrop.RunPass(() => { }), "Passes don't nest.");
                Assert.That(Backdrop.PassActive, Is.True, "A refused nested pass doesn't end the real one.");
            });

            Assert.That(Backdrop.PassActive, Is.False);
            Assert.That(viewport.KsBackdropPassActive, Is.False);
            Assert.That(Backdrop.CanBlur, Is.True);
        });
    }

    [Test]
    public async Task ThePassFlagIsClearedWhenTheDrawingThrows()
    {
        await SetTheme(true);

        await Client.WaitPost(() =>
        {
            Assert.Throws<InvalidOperationException>(() => Backdrop.RunPass(() => throw new InvalidOperationException("draw failed")));

            Assert.That(Backdrop.PassActive, Is.False, "Left set, the world would never be rendered again.");
        });
    }

    [Test]
    public async Task PopupsBlurOnlyUnderTheTguiThemeAndWhenNotTurnedOff()
    {
        await SetTheme(true);
        await Client.WaitPost(() => Assert.That(Backdrop.CanBlur, Is.True));

        await OverrideCVar(Side.Client, KsCCVars.TguiBackdropBlur, false);
        await Client.WaitPost(() => Assert.That(Backdrop.CanBlur, Is.False, "Turned off."));

        await OverrideCVar(Side.Client, KsCCVars.TguiBackdropBlur, true);
        await SetTheme(false);
        await Client.WaitPost(() => Assert.That(Backdrop.CanBlur, Is.False, "Baron has none."));
    }

    [Test]
    public async Task ATooltipAndADropdownAreGivenTheBlur()
    {
        await SetTheme(true);

        await Client.WaitPost(() =>
        {
            var window = new DefaultWindow { SetSize = new Vector2(400f, 300f) };
            var button = new Button { Text = "button", ToolTip = "a tooltip" };
            window.Contents.AddChild(button);
            window.OpenCentered();

            var tooltip = new Tooltip { Text = "a tooltip", Tracking = true };
            UiManager.PopupRoot.AddChild(tooltip);
            Feel.TakeTooltip(tooltip, button);
            Feel.UpdateWindowTooltips(0f);

            var host = (KsTguiTooltipHost)tooltip.Parent!;
            Assert.That(host.Backdrop, Is.SameAs(Backdrop));

            var (option, popup, scaleHost) = OpenDropdown();
            Assert.That(scaleHost.Backdrop, Is.SameAs(Backdrop));

            popup.Close();
            option.Orphan();
            window.Close();
        });
    }

    [Test]
    public async Task AClosedDropdownIsDrawnFadingOutAndShrinkingThenGoes()
    {
        await SetTheme(true);

        await Client.WaitPost(() =>
        {
            var (option, popup, host) = OpenDropdown();
            FinishOpening();
            Assert.That(host.Scale, Is.EqualTo(1f).Within(0.001f));
            Assert.That(Feel.DropdownGhosts, Is.Empty);

            popup.Close();

            Assert.That(Feel.DropdownGhosts, Has.Count.EqualTo(1));
            var ghost = Feel.DropdownGhosts[0];
            Assert.That(ghost.Parent, Is.SameAs(UiManager.PopupRoot), "In the popup layer.");
            Assert.That(ghost.MouseFilter, Is.EqualTo(Control.MouseFilterMode.Ignore), "It takes no clicks.");
            Assert.That(ghost.Popup, Is.SameAs(popup));
            Assert.That(popup.Parent, Is.Null, "The popup is not parented again: the option button reuses it.");
            Assert.That(host.Parent, Is.SameAs(ghost), "The ghost holds the menu while it closes, so it is in the tree with the real UI scale.");

            var alpha = host.Modulate.A;
            var scale = host.Scale;
            for (var i = 1; i <= 3; i++)
            {
                Feel.FrameUpdate(new FrameEventArgs(0.04f));
                Assert.That(host.Modulate.A, Is.LessThan(alpha), "Fading out.");
                Assert.That(host.Scale, Is.LessThan(scale), "Shrinking.");
                alpha = host.Modulate.A;
                scale = host.Scale;
            }

            // tgui's 0.2 s of CSS ease, to opacity 0 and scale(.9): 0.12 s in.
            var eased = KsTguiFeelUIController.CssEase(0.12f / KsTguiDropdownGhost.Seconds);
            Assert.That(host.Modulate.A, Is.EqualTo(1f - eased).Within(0.01f));
            Assert.That(host.Scale, Is.EqualTo(1f - 0.1f * eased).Within(0.01f));

            Feel.FrameUpdate(new FrameEventArgs(0.1f));
            Assert.That(Feel.DropdownGhosts, Is.Empty, "Finished, it is let go.");
            Assert.That(ghost.Parent, Is.Null);
            Assert.That(host.Scale, Is.EqualTo(1f), "The popup is left as an open one would find it.");
            Assert.That(host.Modulate.A, Is.EqualTo(1f));

            option.Orphan();
        });
    }

    [Test]
    public async Task AGhostIsDroppedAtOnceWhenTheDropdownOpensAgain()
    {
        await SetTheme(true);

        await Client.WaitPost(() =>
        {
            var (option, popup, host) = OpenDropdown();
            FinishOpening();
            popup.Close();
            Feel.FrameUpdate(new FrameEventArgs(0.05f));
            var ghost = Feel.DropdownGhosts.Single();

            OpenDropdown(option);

            Assert.That(Feel.DropdownGhosts, Is.Empty, "Dropped, not left to fade over the open menu.");
            Assert.That(ghost.Parent, Is.Null);
            Assert.That(popup.Parent, Is.SameAs(UiManager.ModalRoot), "Open again.");
            Assert.That(popup.Children.Single(), Is.SameAs(host), "Its contents are still in the one host.");
            Assert.That(host.Scale, Is.EqualTo(0.9f), "And the menu opens with its transition again.");
            Assert.That(host.Modulate.A, Is.EqualTo(0f));

            Feel.FrameUpdate(new FrameEventArgs(0.01f));
            Feel.FrameUpdate(new FrameEventArgs(0.3f));
            Assert.That(host.Scale, Is.EqualTo(1f).Within(0.001f));
            Assert.That(Feel.DropdownGhosts, Is.Empty);

            popup.Close();
            option.Orphan();
        });
    }

    [Test]
    public async Task ACloseDuringTheOpeningStartsFromWhereItGot()
    {
        await SetTheme(true);

        await Client.WaitPost(() =>
        {
            var (option, popup, host) = OpenDropdown();
            Feel.FrameUpdate(new FrameEventArgs(0.01f));
            Feel.FrameUpdate(new FrameEventArgs(0.06f));
            var openedTo = host.Modulate.A;
            Assert.That(openedTo, Is.InRange(0.1f, 0.95f));

            popup.Close();
            var ghost = Feel.DropdownGhosts.Single();
            Feel.FrameUpdate(new FrameEventArgs(0.001f));

            Assert.That(host.Modulate.A, Is.EqualTo(openedTo).Within(0.02f), "No jump back to fully shown.");
            Assert.That(ghost.Parent, Is.Not.Null);

            option.Orphan();
        });
    }

    [Test]
    public async Task ANeverShownMenuLeavesNoGhost()
    {
        await SetTheme(true);

        await Client.WaitPost(() =>
        {
            var (option, popup, _) = OpenDropdown();
            popup.Close();

            Assert.That(Feel.DropdownGhosts, Is.Empty, "Nothing was on screen yet to fade out.");
            option.Orphan();
        });
    }

    [Test]
    public async Task GhostsAreDroppedWhenTheThemeChanges()
    {
        await SetTheme(true);

        await Client.WaitPost(() =>
        {
            var (option, popup, host) = OpenDropdown();
            FinishOpening();
            popup.Close();
            Assert.That(Feel.DropdownGhosts, Has.Count.EqualTo(1));
            var ghost = Feel.DropdownGhosts[0];

            Feel.FrameUpdate(new FrameEventArgs(0.05f));
            Feel.ThemeChanged();

            Assert.That(Feel.DropdownGhosts, Is.Empty);
            Assert.That(ghost.Parent, Is.Null);
            Assert.That(host.Backdrop, Is.Null, "Nothing blurs after the theme goes.");
            Assert.That(host.Scale, Is.EqualTo(1f));
            Assert.That(host.Modulate.A, Is.EqualTo(1f));

            option.Orphan();
        });
    }

    [Test]
    public async Task NoGhostIsMadeUnderBaronEvenForAPopupThatWasOpenedUnderTgui()
    {
        await SetTheme(true);

        OptionButton option = null!;
        await Client.WaitPost(() =>
        {
            OptionButton popupOption;
            (popupOption, var popup, _) = OpenDropdown();
            option = popupOption;
            FinishOpening();
            popup.Close();
            Feel.ThemeChanged();
        });

        await SetTheme(false);

        await Client.WaitPost(() =>
        {
            var (_, popup, host) = OpenDropdown(option);
            Assert.That(host.Modulate.A, Is.EqualTo(1f), "Opened as it stands: no transition under Baron.");
            popup.Close();

            Assert.That(Feel.DropdownGhosts, Is.Empty);
            option.Orphan();
        });
    }
}
