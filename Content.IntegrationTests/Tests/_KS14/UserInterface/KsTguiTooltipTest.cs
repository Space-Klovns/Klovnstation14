using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Tooltips;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     tgui's tooltips in windows: placed against their control, kept inside the window, opening after 200 ms of rest, and
///         scaling and fading in and out. A headless client has no mouse to make the engine show one, so the tests hand the
///         controller a tooltip as the engine would.
/// </summary>
[TestOf(typeof(KsTguiFeelUIController))]
public sealed class KsTguiTooltipTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly UIBox2 Bounds = new(0f, 0f, 400f, 300f);
    private static readonly Vector2 TooltipSize = new(100f, 20f);

    private IUserInterfaceManager UiManager => Client.ResolveDependency<IUserInterfaceManager>();

    private KsTguiFeelUIController Feel => UiManager.GetUIController<KsTguiFeelUIController>();

    [Test]
    public void ATooltipOpensCentredBelowItsControl()
    {
        var (topLeft, placement) = KsTguiFeelUIController.Place(TooltipSize, new UIBox2(50f, 50f, 150f, 70f), Bounds, KsTguiTooltipPlacement.Bottom);

        Assert.That(placement, Is.EqualTo(KsTguiTooltipPlacement.Bottom));
        Assert.That(topLeft, Is.EqualTo(new Vector2(50f, 76f)), "Centred, 6px below.");
    }

    [Test]
    public void ATooltipWithNoRoomBelowFlipsAbove()
    {
        var (topLeft, placement) = KsTguiFeelUIController.Place(TooltipSize, new UIBox2(50f, 260f, 150f, 280f), Bounds, KsTguiTooltipPlacement.Bottom);

        Assert.That(placement, Is.EqualTo(KsTguiTooltipPlacement.Top));
        Assert.That(topLeft.Y, Is.EqualTo(260f - 6f - 20f));
    }

    [Test]
    public void ATooltipThatWouldFitNeitherWayTakesTheSideItOverflowsLeast()
    {
        var tall = new Vector2(100f, 290f);
        var (_, placement) = KsTguiFeelUIController.Place(tall, new UIBox2(50f, 20f, 150f, 40f), Bounds, KsTguiTooltipPlacement.Top);

        Assert.That(placement, Is.EqualTo(KsTguiTooltipPlacement.Bottom));
    }

    [Test]
    public void ATooltipSlidesAlongToStayInTheWindow()
    {
        var (leftEdge, _) = KsTguiFeelUIController.Place(TooltipSize, new UIBox2(0f, 50f, 20f, 70f), Bounds, KsTguiTooltipPlacement.Bottom);
        var (rightEdge, _) = KsTguiFeelUIController.Place(TooltipSize, new UIBox2(380f, 50f, 400f, 70f), Bounds, KsTguiTooltipPlacement.Bottom);

        Assert.That(leftEdge.X, Is.EqualTo(0f));
        Assert.That(rightEdge.X, Is.EqualTo(300f));
    }

    [Test]
    public void ARightTooltipFlipsLeftAtTheWindowsEdge()
    {
        var (fits, fitsPlacement) = KsTguiFeelUIController.Place(TooltipSize, new UIBox2(50f, 50f, 70f, 70f), Bounds, KsTguiTooltipPlacement.Right);
        var (flipped, flippedPlacement) = KsTguiFeelUIController.Place(TooltipSize, new UIBox2(300f, 50f, 320f, 70f), Bounds, KsTguiTooltipPlacement.Right);

        Assert.That(fitsPlacement, Is.EqualTo(KsTguiTooltipPlacement.Right));
        Assert.That(fits, Is.EqualTo(new Vector2(76f, 50f)), "6px right, centred on the control's height.");
        Assert.That(flippedPlacement, Is.EqualTo(KsTguiTooltipPlacement.Left));
        Assert.That(flipped.X, Is.EqualTo(300f - 6f - 100f));
    }

    [Test]
    public void TheEasingIsCssEase()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KsTguiFeelUIController.CssEase(0f), Is.EqualTo(0f));
            Assert.That(KsTguiFeelUIController.CssEase(1f), Is.EqualTo(1f));

            // Values of cubic-bezier(0.25, 0.1, 0.25, 1), as browsers compute them.
            Assert.That(KsTguiFeelUIController.CssEase(0.25f), Is.EqualTo(0.4094f).Within(0.002f));
            Assert.That(KsTguiFeelUIController.CssEase(0.5f), Is.EqualTo(0.8024f).Within(0.002f));
        });
    }

    /// <summary>
    ///     The stacked rings add up to a blurred edge: a blurred box is at half its alpha on its edge, so a quarter of the 0.5
    ///         shadow next to the box, and less further out.
    /// </summary>
    [Test]
    public void TheShadowFadesOutwards()
    {
        var alphas = KsTguiTooltipHost.BuildLayerAlphas();

        var coverage = new float[alphas.Length];
        var outside = 1f;
        for (var i = alphas.Length - 1; i >= 0; i--)
        {
            outside *= 1f - alphas[i];
            coverage[i] = 1f - outside;
        }

        Assert.That(coverage[0], Is.InRange(0.2f, 0.25f), "Next to the box, a little under a quarter.");
        for (var i = 1; i < coverage.Length; i++)
            Assert.That(coverage[i], Is.LessThan(coverage[i - 1]), $"Band {i} should be lighter than the one inside it.");

        Assert.That(coverage[^1], Is.LessThan(0.01f), "The outermost band should be all but gone.");
    }

    private async Task<(DefaultWindow Window, Button Button)> OpenWindowWithButton()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        DefaultWindow window = null;
        Button button = null;
        await Client.WaitPost(() =>
        {
            window = new DefaultWindow { SetSize = new Vector2(400f, 300f) };
            button = new Button { Text = "button", ToolTip = "a tooltip", HorizontalAlignment = Control.HAlignment.Center, VerticalAlignment = Control.VAlignment.Top };
            window.Contents.AddChild(button);
            window.OpenCentered();
        });
        await Pair.RunTicksSync(2);
        return (window, button);
    }

    /// <summary>
    ///     What the engine does as a tooltip shows: puts it in the popup layer.
    /// </summary>
    private Tooltip ShowTooltip(Control target)
    {
        var tooltip = new Tooltip { Text = target.ToolTip, Tracking = true };
        UiManager.PopupRoot.AddChild(tooltip);
        Feel.TakeTooltip(tooltip, target);
        return tooltip;
    }

    [Test]
    public async Task AWindowsTooltipOpensBelowItsControlAndScalesIn()
    {
        var (window, button) = await OpenWindowWithButton();

        await Client.WaitPost(() =>
        {
            var tooltip = ShowTooltip(button);
            Feel.UpdateWindowTooltips(0f);

            var host = tooltip.Parent as KsTguiTooltipHost;
            Assert.That(host, Is.Not.Null, "The tooltip should be taken into a host.");
            Assert.That(host!.Parent, Is.EqualTo(UiManager.PopupRoot));
            Assert.That(tooltip.Tracking, Is.False, "A window's tooltip stays put rather than following the mouse.");
            Assert.That(tooltip.MaxWidth, Is.EqualTo(KsTguiTooltip.MaxWidth));
            Assert.That(host.Modulate.A, Is.EqualTo(0f));
            Assert.That(host.Scale, Is.EqualTo(KsTguiTooltip.StartScale));

            Feel.UpdateWindowTooltips(0.1f);
            Assert.That(host.Modulate.A, Is.InRange(0.5f, 0.95f), "Halfway through, it should be partly faded in.");
            Assert.That(host.Scale, Is.InRange(KsTguiTooltip.StartScale + 0.01f, 0.999f));

            Feel.UpdateWindowTooltips(0.2f);
            Assert.That(host.Modulate.A, Is.EqualTo(1f));
            Assert.That(host.Scale, Is.EqualTo(1f));
        });
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            var host = UiManager.PopupRoot.Children.OfType<KsTguiTooltipHost>().Single(host => host.Target == button);
            var buttonBox = UIBox2.FromDimensions(button.GlobalPosition, button.Size);

            Assert.That(host.GlobalPosition.Y, Is.EqualTo(buttonBox.Bottom + KsTguiTooltip.Offset).Within(0.5f), "6px below the button.");
            Assert.That(host.GlobalPosition.X + host.Size.X / 2f, Is.EqualTo(buttonBox.Center.X).Within(0.5f), "Centred on it.");
            window.Close();
        });
    }

    [Test]
    public async Task AHiddenTooltipFadesOutThenGoes()
    {
        var (window, button) = await OpenWindowWithButton();

        await Client.WaitPost(() =>
        {
            var tooltip = ShowTooltip(button);
            Feel.UpdateWindowTooltips(0f);
            Feel.UpdateWindowTooltips(0.3f);
            var host = (KsTguiTooltipHost)tooltip.Parent!;

            // The engine hides it.
            tooltip.Orphan();
            Feel.UpdateWindowTooltips(0.1f);

            Assert.That(tooltip.Parent, Is.EqualTo(host), "It should be put back to fade out.");
            Assert.That(host.Closing, Is.True);
            Assert.That(host.Modulate.A, Is.InRange(0.05f, 0.95f));
            Assert.That(host.Scale, Is.LessThan(1f));

            Feel.UpdateWindowTooltips(0.2f);
            Assert.That(tooltip.Parent, Is.Null, "Faded out, it should be let go.");
            Assert.That(host.Parent, Is.Null);
            window.Close();
        });
    }

    /// <summary>
    ///     Some controls hand out the same tooltip every time, and the engine can't add one that's still parented. So when the
    ///         mouse is back on the control, a fading tooltip is let go before the engine shows it again.
    /// </summary>
    [Test]
    public async Task AFadingTooltipIsLetGoWhenItsControlIsHoveredAgain()
    {
        var (window, button) = await OpenWindowWithButton();

        await Client.WaitPost(() =>
        {
            var tooltip = ShowTooltip(button);
            Feel.UpdateWindowTooltips(0f);
            Feel.UpdateWindowTooltips(0.3f);

            tooltip.Orphan();
            Feel.UpdateWindowTooltips(0.05f);
            Assert.That(tooltip.Parent, Is.Not.Null);

            UiManager.SetHovered(button);
            Feel.UpdateWindowTooltips(0.01f);
            Assert.That(tooltip.Parent, Is.Null, "The engine must be able to add it again.");

            UiManager.SetHovered(null);
            window.Close();
        });
    }

    [Test]
    public async Task ATooltipOfAControlThatWentIsNotFaded()
    {
        var (window, button) = await OpenWindowWithButton();

        await Client.WaitPost(() =>
        {
            var tooltip = ShowTooltip(button);
            Feel.UpdateWindowTooltips(0f);

            window.Close();
            tooltip.Orphan();
            Feel.UpdateWindowTooltips(0.01f);

            Assert.That(tooltip.Parent, Is.Null);
            Assert.That(UiManager.PopupRoot.Children.OfType<KsTguiTooltipHost>().Where(host => host.Target == button), Is.Empty);
        });
    }

    [Test]
    public async Task ControlsInAWindowGetTgsDelay()
    {
        var (window, button) = await OpenWindowWithButton();

        await Client.WaitPost(() =>
        {
            var ownDelay = new Button { ToolTip = "own", TooltipDelay = 1f };
            var noTooltip = new Button();
            window.Contents.AddChild(ownDelay);
            window.Contents.AddChild(noTooltip);

            KsTguiFeelUIController.SetTooltipDelays(window);
            Assert.That(button.TooltipDelay, Is.EqualTo(KsTguiTooltip.Delay));
            Assert.That(ownDelay.TooltipDelay, Is.EqualTo(1f), "A control's own delay is kept.");
            Assert.That(noTooltip.TooltipDelay, Is.Null);

            KsTguiFeelUIController.ResetTooltipDelays(window);
            Assert.That(button.TooltipDelay, Is.Null, "Only what was set is taken back.");
            Assert.That(ownDelay.TooltipDelay, Is.EqualTo(1f));
            window.Close();
        });
    }

    /// <summary>
    ///     The engine reads a control's delay as the mouse enters it, so the very first control hovered in a window, before
    ///         any delay was set, must still open at tg's 200 ms rather than the engine's 250. The headless client re-reads the
    ///         hovered control from its mouse position every frame, so this checks the delay the engine is counting towards,
    ///         a private field, rather than waiting for the tooltip.
    /// </summary>
    [Test]
    public async Task TheFirstControlHoveredInAWindowOpensAtTgsDelay()
    {
        var (window, button) = await OpenWindowWithButton();

        await Client.WaitPost(() =>
        {
            Assert.That(button.TooltipDelay, Is.Null, "Nothing has been hovered in this window yet.");
            UiManager.SetHovered(button);
            Assert.That(EngineTooltipDelay(), Is.Not.EqualTo(KsTguiTooltip.Delay), "The engine read its own default on entry.");

            Feel.UpdateWindowTooltips(0f);
            Assert.That(UiManager.CurrentlyHovered, Is.EqualTo(button), "Still hovered after being entered again.");
            Assert.That(EngineTooltipDelay(), Is.EqualTo(KsTguiTooltip.Delay), "The engine counts to 200 ms, not 250.");

            UiManager.SetHovered(null);
            window.Close();
        });
    }

    private float? EngineTooltipDelay()
    {
        var field = UiManager.GetType().GetField("_tooltipDelay", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "The engine's tooltip delay field was renamed; update this test.");
        return (float?)field.GetValue(UiManager);
    }

    private KsTguiTooltipHost HostOf(Control target)
    {
        return UiManager.PopupRoot.Children.OfType<KsTguiTooltipHost>().FirstOrDefault(host => host.Target == target);
    }

    /// <summary>
    ///     Off the tgui theme, nothing is taken over.
    /// </summary>
    [Test]
    public async Task TurningTheThemeOffLetsGoOfEveryTooltip()
    {
        var (window, button) = await OpenWindowWithButton();

        Tooltip tooltip = null;
        await Client.WaitPost(() =>
        {
            tooltip = ShowTooltip(button);
            Feel.UpdateWindowTooltips(0f);
            KsTguiFeelUIController.SetTooltipDelays(window);
        });

        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, false);
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            Assert.That(UiManager.PopupRoot.Children.OfType<KsTguiTooltipHost>(), Is.Empty);
            Assert.That(tooltip.Parent, Is.Null);
            Assert.That(button.TooltipDelay, Is.Null);
            window.Close();
        });
    }

    /// <summary>
    ///     The way the engine shows one: the mouse on the control, then the tooltip into the popup layer.
    /// </summary>
    [Test]
    public async Task AnEngineTooltipInAWindowIsTakenOver()
    {
        var (window, button) = await OpenWindowWithButton();

        await Client.WaitPost(() =>
        {
            UiManager.SetHovered(button);
            var tooltip = new Tooltip { Text = button.ToolTip };
            UiManager.PopupRoot.AddChild(tooltip);
            Feel.UpdateWindowTooltips(0f);

            Assert.That(tooltip.Parent, Is.InstanceOf<KsTguiTooltipHost>());
            UiManager.SetHovered(null);
            window.Close();
        });
    }

    /// <summary>
    ///     KS14's diegetic windows, like the nuke's, aren't tgui: their tooltips stay the engine's.
    /// </summary>
    [Test]
    public async Task ADiegeticWindowsTooltipIsLeftAlone()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        await Client.WaitPost(() =>
        {
            var window = new Content.Client._KS14.Shuttles.UI.KsBaseWindow { SetSize = new Vector2(200f, 200f) };
            var button = new Button { Text = "button", ToolTip = "a tooltip" };
            window.AddChild(button);
            window.OpenCentered();

            UiManager.SetHovered(button);
            var tooltip = new Tooltip { Text = button.ToolTip };
            UiManager.PopupRoot.AddChild(tooltip);
            Feel.UpdateWindowTooltips(0f);

            Assert.That(tooltip.Parent, Is.EqualTo(UiManager.PopupRoot));
            UiManager.SetHovered(null);
            tooltip.Orphan();
            window.Close();
        });
    }

    [Test]
    public async Task ClosingTheWindowDropsItsShownTooltip()
    {
        var (window, button) = await OpenWindowWithButton();

        await Client.WaitPost(() =>
        {
            var tooltip = ShowTooltip(button);
            Feel.UpdateWindowTooltips(0f);
            Feel.UpdateWindowTooltips(0.3f);

            window.Close();
            Feel.UpdateWindowTooltips(0.01f);

            Assert.That(tooltip.Parent, Is.Null);
            Assert.That(UiManager.PopupRoot.Children.OfType<KsTguiTooltipHost>().Where(host => host.Target == button), Is.Empty);
        });
    }
}
