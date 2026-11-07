using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Windows.Apc;
using Content.Client._KS14.Tgui.Windows.Chem;
using Content.Client._KS14.Tgui.Windows.Pda;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     tgui's scrolled boxes: the bar's own 15px column, a track with nothing to scroll for <c>overflow-y: scroll</c>, and
///         Tabs <c>scrollable</c>. Layout runs headlessly with real font metrics; nothing draws, so the fade is pinned by the
///         widths the control reports for it, not by pixels.
/// </summary>
[TestOf(typeof(KsTguiScrollContainer))]
public sealed class KsTguiScrollTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private IUserInterfaceManager UiManager => Client.ResolveDependency<IUserInterfaceManager>();

    private async Task SetTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);
    }

    /// <summary>
    ///     Remembers the width it was last measured at, and takes the height it was told to.
    /// </summary>
    private sealed class Probe : Control
    {
        private float _height = 20f;
        private float _width = 50f;

        public float Height
        {
            get => _height;
            set
            {
                _height = value;
                InvalidateMeasure();
            }
        }

        public float Width
        {
            get => _width;
            set
            {
                _width = value;
                InvalidateMeasure();
            }
        }

        public Vector2 LastAvailable;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            LastAvailable = availableSize;
            return new Vector2(Width, Height);
        }
    }

    private static readonly Vector2 Box = new(100f, 100f);

    /// <summary>
    ///     A container in a fresh root, laid out at <see cref="Box"/>: out-of-tree controls only queue their invalidation.
    /// </summary>
    private KsTguiScrollContainer Scroll(Probe probe, bool reserve, bool horizontal = false, bool reserveHorizontal = false)
    {
        var scroll = new KsTguiScrollContainer
        {
            VScrollEnabled = true,
            HScrollEnabled = horizontal,
            ReserveVertical = reserve,
            ReserveHorizontal = reserveHorizontal,
            MinSize = Box,
            MaxSize = Box,
            Children = { probe },
        };
        UiManager.PopupRoot.AddChild(scroll);
        Layout(scroll);
        return scroll;
    }

    private static void Layout(Control control)
    {
        foreach (var child in control.Children)
        {
            if (child is ScrollBar bar)
                bar.ForceRunStyleUpdate();
        }

        control.InvalidateMeasure();
        control.InvalidateArrange();
        control.Measure(Box);
        control.Arrange(UIBox2.FromDimensions(Vector2.Zero, Box));
    }

    private static T Bar<T>(Control scroll) where T : ScrollBar
    {
        foreach (var child in scroll.Children)
        {
            if (child is T bar)
                return bar;
        }

        throw new InvalidOperationException();
    }

    [Test]
    public async Task ReservedVerticalBarTakesItsColumnWithNothingToScroll()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            var probe = new Probe();
            var scroll = Scroll(probe, reserve: true);
            var bar = Bar<VScrollBar>(scroll);

            Assert.Multiple(() =>
            {
                Assert.That(probe.Width, Is.EqualTo(50f));
                Assert.That(probe.Size.X, Is.EqualTo(85f), "The content is arranged 15px narrower");
                Assert.That(probe.LastAvailable.X, Is.EqualTo(85f), "and was measured at the width it is arranged at");
                Assert.That(bar.Visible, Is.True, "The track shows although nothing overflows");
                Assert.That(bar.Position.X, Is.EqualTo(85f));
                Assert.That(bar.Width, Is.EqualTo(15f));
                Assert.That(bar.HasStyleClass(KsTguiScrollContainer.StyleClassDisabled), Is.True, "with no thumb and pale arrows");
                Assert.That(scroll.VerticalOverflows, Is.False);
            });

            scroll.Orphan();
        });
    }

    [Test]
    public async Task AutoBarIsOnlyThereWhileTheContentOverflows()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            var probe = new Probe();
            var scroll = Scroll(probe, reserve: false);
            var bar = Bar<VScrollBar>(scroll);

            Assert.That(bar.Visible, Is.False, "overflow-y: auto shows nothing while it fits");
            Assert.That(probe.Size.X, Is.EqualTo(100f));

            probe.Height = 400f;
            Layout(scroll);

            Assert.Multiple(() =>
            {
                Assert.That(bar.Visible, Is.True, "The track shows while it overflows");
                Assert.That(probe.Size.X, Is.EqualTo(85f), "The bar then takes its column");
                Assert.That(probe.LastAvailable.X, Is.EqualTo(85f));
                Assert.That(bar.Position.X, Is.EqualTo(85f));
                Assert.That(bar.HasStyleClass(KsTguiScrollContainer.StyleClassDisabled), Is.False);
                Assert.That(bar.Page, Is.EqualTo(100f));
                Assert.That(bar.MaxValue, Is.EqualTo(400f));
            });

            scroll.Orphan();
        });
    }

    [Test]
    public async Task ScrollingMovesTheContentAndSurvivesRelayout()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            var probe = new Probe { Height = 400f };
            var scroll = Scroll(probe, reserve: true);

            scroll.VScroll = 130f;
            Layout(scroll);
            Assert.That(probe.Position.Y, Is.EqualTo(-130f));

            // Shrinking the content so that it only just overflows clamps the value rather than losing the room.
            probe.Height = 150f;
            probe.InvalidateMeasure();
            Layout(scroll);
            Assert.That(probe.Position.Y, Is.EqualTo(-50f));

            probe.Height = 20f;
            Layout(scroll);
            Assert.That(probe.Position.Y, Is.EqualTo(0f), "Nothing to scroll: back to the top");
            Assert.That(scroll.GetScrollValue(), Is.EqualTo(Vector2.Zero));

            scroll.Orphan();
        });
    }

    [Test]
    public async Task HorizontalBarTakesItsRowToo()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            var probe = new Probe { Width = 300f, Height = 20f };
            var scroll = Scroll(probe, reserve: true, horizontal: true, reserveHorizontal: true);
            var vertical = Bar<VScrollBar>(scroll);
            var horizontal = Bar<HScrollBar>(scroll);

            Assert.Multiple(() =>
            {
                Assert.That(horizontal.Visible, Is.True);
                Assert.That(horizontal.Height, Is.EqualTo(15f));
                Assert.That(horizontal.Position.Y, Is.EqualTo(85f));
                Assert.That(probe.Size.Y, Is.EqualTo(85f), "The content is a row shorter");
                Assert.That(probe.Size.X, Is.EqualTo(300f), "and as wide as it needs");
                Assert.That(scroll.HorizontalOverflows, Is.True);
                Assert.That(horizontal.HasStyleClass(KsTguiScrollContainer.StyleClassDisabled), Is.False);
                Assert.That(vertical.HasStyleClass(KsTguiScrollContainer.StyleClassDisabled), Is.True);
                Assert.That(vertical.Height, Is.LessThanOrEqualTo(85f), "The vertical bar stops above the horizontal one");
            });

            scroll.Orphan();
        });
    }

    [Test]
    public async Task TheWheelGoesOnWhenThereIsNothingToScroll()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            var probe = new Probe();
            var scroll = Scroll(probe, reserve: true);

            Assert.That(Wheel(scroll), Is.False, "A browser's reserved bar with nothing to scroll lets the wheel through");

            probe.Height = 400f;
            Layout(scroll);
            Assert.That(Wheel(scroll), Is.True);
            Assert.That(scroll.VScrollTarget, Is.GreaterThan(0f));

            scroll.Orphan();
        });
    }

    private bool Wheel(Control control)
    {
        var args = new GUIMouseWheelEventArgs(new Vector2(0f, -1f), control, Vector2.Zero, default, Vector2.Zero, Vector2.Zero);
        typeof(Control).GetMethod("MouseWheel", Any)!.Invoke(control, [args]);
        return args.Handled;
    }

    [Test]
    public async Task SectionScrollableTakesTheColumnOnlyWhenItOverflows()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            var probe = new Probe { Height = 20f, HorizontalExpand = true };
            var section = new KsTguiSection { Title = "S", Scrollable = true, Fill = true, MinSize = new Vector2(200f, 120f), MaxSize = new Vector2(200f, 120f) };
            section.AddChild(probe);
            UiManager.PopupRoot.AddChild(section);
            section.InvalidateMeasure();
            section.InvalidateArrange();
            section.Measure(new Vector2(200f, 120f));
            section.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(200f, 120f)));
            var wide = probe.Size.X;

            probe.Height = 600f;

            // The queue the UI manager would run: the change reaches the section through each parent.
            for (var parent = probe.Parent; parent != null && parent != UiManager.PopupRoot; parent = parent.Parent)
            {
                parent.InvalidateMeasure();
                parent.InvalidateArrange();
            }

            section.InvalidateMeasure();
            section.InvalidateArrange();
            section.Measure(new Vector2(200f, 120f));
            section.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(200f, 120f)));

            var dbg = new List<string>();
            for (Control c = probe; c != section.Parent; c = c.Parent!)
                dbg.Add($"{c.GetType().Name} size={c.Size} desired={c.DesiredSize}");
            Assert.That(probe.Size.X, Is.EqualTo(wide - 15f), "Overflowing, the body gives up 15px for the bar. DBG " + string.Join(" | ", dbg));
            Assert.That(probe.LastAvailable.X, Is.EqualTo(probe.Size.X));

            section.Orphan();
        });
    }

    [Test]
    public async Task WindowsWhoseTgOriginalIsOverflowYScrollReserveTheColumn()
    {
        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Reserving(new KsTguiApcWindow()), Is.True, "Apc.jsx: Window.Content scrollable");
                Assert.That(Reserving(new KsTguiChemMasterWindow()), Is.True, "ChemMaster.tsx: Window.Content scrollable");
                Assert.That(Reserving(new KsTguiPdaWindow()), Is.True, "NtosMain.tsx: NtosWindow.Content scrollable");
            });
        });
    }

    private static bool Reserving(Control window)
    {
        var found = new List<KsTguiScrollContainer>();
        Collect(window, found);
        return found.Exists(scroll => scroll.ReserveVertical);
    }

    private static void Collect(Control control, List<KsTguiScrollContainer> into)
    {
        if (control is KsTguiScrollContainer scroll)
            into.Add(scroll);

        foreach (var child in control.Children)
            Collect(child, into);
    }

    // Tabs scrollable

    private KsTguiTabs Strip(int count, float width, bool scrollable = true)
    {
        var tabs = new KsTguiTabs { Scrollable = scrollable };
        for (var i = 0; i < count; i++)
            tabs.AddTab("Tab number " + i);

        var host = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, MinSize = new Vector2(width, 0f), MaxSize = new Vector2(width, 100f) };
        host.AddChild(tabs);
        tabs.HorizontalExpand = true;
        UiManager.PopupRoot.AddChild(host);
        LayoutStrip(tabs);
        return tabs;
    }

    private static void LayoutStrip(KsTguiTabs tabs)
    {
        var host = tabs.Parent!;
        host.InvalidateMeasure();
        host.InvalidateArrange();
        host.Measure(new Vector2(host.MaxWidth, 100f));
        host.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(host.MaxWidth, 100f)));
    }

    private async Task Settle(KsTguiTabs tabs)
    {
        for (var i = 0; i < 200 && MathF.Abs(tabs.ScrollTarget - tabs.ScrollOffset) > 0.01f; i++)
            await Pair.RunTicksSync(1);

        LayoutStrip(tabs);
    }

    [Test]
    public async Task ScrollableTabsFadeAndOfferAButtonOnTheSideWithMore()
    {
        await Client.WaitAssertion(() =>
        {
            var tabs = Strip(10, 300f);

            Assert.Multiple(() =>
            {
                Assert.That(tabs.Width, Is.EqualTo(300f), "The strip is the room it is given, not the tabs'");
                Assert.That(tabs.LeftFade, Is.EqualTo(0f));
                Assert.That(tabs.RightFade, Is.EqualTo(KsTguiTabs.MaskWidth), "3.25em of 12px");
                Assert.That(tabs.ScrollButtonsShown, Is.EqualTo((false, true)));
            });

            tabs.Parent!.Orphan();
        });
    }

    [Test]
    public async Task TabsThatFitHaveNoFadeAndNoButtons()
    {
        await Client.WaitAssertion(() =>
        {
            var tabs = Strip(2, 600f);

            Assert.Multiple(() =>
            {
                Assert.That(tabs.LeftFade, Is.EqualTo(0f));
                Assert.That(tabs.RightFade, Is.EqualTo(0f));
                Assert.That(tabs.ScrollButtonsShown, Is.EqualTo((false, false)));
            });

            tabs.Parent!.Orphan();
        });
    }

    [Test]
    public async Task TheRightButtonScrollsByHalfTheWidthAndTheFadeFollows()
    {
        KsTguiTabs tabs = null!;
        await Client.WaitPost(() => tabs = Strip(10, 300f));

        await Client.WaitPost(() => Press(tabs.ScrollRightButton));
        Assert.That(tabs.ScrollTarget, Is.EqualTo(150f), "Half of the 300px");

        await Settle(tabs);

        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(tabs.ScrollOffset, Is.EqualTo(150f).Within(0.01f));
                Assert.That(tabs.LeftFade, Is.EqualTo(KsTguiTabs.MaskWidth));
                Assert.That(tabs.RightFade, Is.EqualTo(KsTguiTabs.MaskWidth), "More both ways");
                Assert.That(tabs.ScrollButtonsShown, Is.EqualTo((true, true)));
            });
        });

        // Far enough to reach the end: the right side loses its fade and button.
        for (var i = 0; i < 10; i++)
        {
            await Client.WaitPost(() => Press(tabs.ScrollRightButton));
            await Settle(tabs);
        }

        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(tabs.RightFade, Is.EqualTo(0f));
                Assert.That(tabs.LeftFade, Is.EqualTo(KsTguiTabs.MaskWidth));
                Assert.That(tabs.ScrollButtonsShown, Is.EqualTo((true, false)));
            });

            tabs.Parent!.Orphan();
        });
    }

    [Test]
    public async Task TheLeftButtonScrollsBackToTheStart()
    {
        KsTguiTabs tabs = null!;
        await Client.WaitPost(() => tabs = Strip(10, 300f));

        await Client.WaitPost(() => Press(tabs.ScrollRightButton));
        await Settle(tabs);
        await Client.WaitPost(() => Press(tabs.ScrollLeftButton));
        await Settle(tabs);

        await Client.WaitAssertion(() =>
        {
            Assert.That(tabs.ScrollOffset, Is.EqualTo(0f).Within(0.01f));
            Assert.That(tabs.LeftFade, Is.EqualTo(0f));
            Assert.That(tabs.ScrollButtonsShown, Is.EqualTo((false, true)));

            tabs.Parent!.Orphan();
        });
    }

    [Test]
    public async Task TheWheelScrollsTheStripSidewaysAtOnce()
    {
        await Client.WaitAssertion(() =>
        {
            var tabs = Strip(10, 300f);

            var args = new GUIMouseWheelEventArgs(new Vector2(0f, -1f), tabs, Vector2.Zero, default, Vector2.Zero, Vector2.Zero);
            typeof(Control).GetMethod("MouseWheel", Any)!.Invoke(tabs, [args]);

            Assert.That(args.Handled, Is.True);
            Assert.That(tabs.ScrollOffset, Is.EqualTo(100f), "A notch down is a notch right");
            Assert.That(tabs.ScrollTarget, Is.EqualTo(100f), "with no easing");

            tabs.Parent!.Orphan();
        });
    }

    [Test]
    public async Task TheSelectedTabIsBroughtToTheMiddle()
    {
        KsTguiTabs tabs = null!;
        await Client.WaitPost(() => tabs = Strip(10, 300f));

        await Client.WaitPost(() => tabs.Select((KsTguiTabItem)tabs.GetChild(5)));
        await Pair.RunTicksSync(2);
        await Settle(tabs);

        await Client.WaitAssertion(() =>
        {
            var selected = (KsTguiTabItem)tabs.GetChild(5);
            var centre = selected.Position.X + selected.Width / 2f;
            Assert.That(centre, Is.EqualTo(150f).Within(1f), "Centred in the 300px strip");

            tabs.Parent!.Orphan();
        });
    }

    [Test]
    public async Task ScrollableIsOffForVerticalTabsAndLeavesTheChildrenAlone()
    {
        await Client.WaitAssertion(() =>
        {
            var vertical = new KsTguiTabs { Scrollable = true, Vertical = true };
            vertical.AddTab("a");
            vertical.AddTab("b");
            Assert.That(vertical.ChildCount, Is.EqualTo(2), "Nothing but the tabs are children");
            Assert.That(vertical.RightFade, Is.EqualTo(0f));

            var plain = new KsTguiTabs();
            plain.AddTab("a");
            Assert.That(plain.ChildCount, Is.EqualTo(1));

            var scrolling = new KsTguiTabs { Scrollable = true };
            scrolling.AddTab("a");
            scrolling.ClearTabs();
            Assert.That(scrolling.SelectedIndex, Is.EqualTo(-1));
            Assert.That(scrolling.ChildCount, Is.EqualTo(1), "Only the fade and buttons are left");
            scrolling.AddTab("b");
            Assert.That(scrolling.GetChild(0), Is.TypeOf<KsTguiTabItem>(), "Tabs come before the fade, so child 0 stays a tab");
        });
    }

    private static void Press(BaseButton button)
    {
        var field = typeof(BaseButton).GetField(nameof(BaseButton.OnPressed), BindingFlags.Instance | BindingFlags.NonPublic);
        var handler = (Action<BaseButton.ButtonEventArgs>?)field!.GetValue(button);
        handler?.Invoke(new BaseButton.ButtonEventArgs(button, null!));
    }
}
