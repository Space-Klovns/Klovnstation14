using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client.Communications.UI;
using Content.Client.SurveillanceCamera.UI;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     Buttons stacked in a vertical box touched under tgui: a box's separation is 0 and the sheet gives a plain button no
///         bottom margin. <see cref="KsTguiSheetlet.StyleClassButtonStack"/> spaces them under tgui and leaves Baron, whose
///         framed buttons look apart already, as it was.
/// </summary>
[TestOf(typeof(KsTguiSheetlet))]
public sealed class KsTguiButtonStackTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (var child in root.Children)
        {
            if (child is T match)
                yield return match;

            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }

    private static float Gap(Control above, Control below)
    {
        return below.GlobalPosition.Y - (above.GlobalPosition.Y + above.Size.Y);
    }

    private async Task<float> GapIn(bool tgui, System.Func<BaseWindow> open, System.Func<BaseWindow, (Control Above, Control Below)> pick)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        BaseWindow window = null;
        await Client.WaitPost(() =>
        {
            window = open();
            window.OpenCentered();
        });
        await Pair.RunTicksSync(5);

        var gap = 0f;
        await Client.WaitPost(() =>
        {
            var (above, below) = pick(window);
            gap = Gap(above, below);
            window.Close();
        });

        return gap;
    }

    private static Button ButtonWithText(Control root, string locId)
    {
        var text = Robust.Shared.Localization.Loc.GetString(locId);
        return Descendants<Button>(root).First(button => button.Text == text);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task CommunicationsConsoleButtonsAreSpacedUnderTgui(bool tgui)
    {
        var gap = await GapIn(tgui,
            () => new CommunicationsConsoleMenu(),
            window => (((CommunicationsConsoleMenu)window).AnnounceButton, ((CommunicationsConsoleMenu)window).BroadcastButton));

        if (tgui)
            Assert.That(gap, Is.EqualTo(2f).Within(0.01f), "tg's 2px between stacked buttons.");
        else
            Assert.That(gap, Is.EqualTo(0f).Within(0.01f), "Baron keeps its layout.");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task CameraMonitorButtonsAreSpacedUnderTgui(bool tgui)
    {
        var gap = await GapIn(tgui,
            () => new SurveillanceCameraMonitorWindow(),
            window => (Descendants<OptionButton>(window).First(), ButtonWithText(window, "surveillance-camera-monitor-ui-refresh-subnets")));

        if (tgui)
            Assert.That(gap, Is.EqualTo(2f).Within(0.01f), "tg's 2px between stacked buttons.");
        else
            Assert.That(gap, Is.EqualTo(0f).Within(0.01f), "Baron keeps its layout.");
    }
}
