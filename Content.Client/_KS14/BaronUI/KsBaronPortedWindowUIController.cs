using System.Numerics;
using Content.Client._KS14.Tgui;
using Content.Client.UserInterface.Controls;
using Content.Shared._KS14.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;

namespace Content.Client._KS14.BaronUI;

/// <summary>
///     Gives a ported tgui window opened under Baron the room Baron's taller header takes. The ports state tg's own
///         <c>width</c>/<c>height</c>, which assume tgui's 32px title bar; Baron's frame is about 40px taller, so the same
///         height would leave the contents that much short. Under tgui nothing here runs, so tg's sizes are unchanged.
/// </summary>
/// <remarks>
///     Done once per window, when it is first opened under Baron. A window open when the theme is switched keeps its size
///         until it is reopened, as everything else about it does.
/// </remarks>
public sealed partial class KsBaronPortedWindowUIController : UIController
{
    /// <summary>
    ///     Where the ported windows live; only they were sized for tgui's title bar.
    /// </summary>
    private const string PortedNamespace = "Content.Client._KS14.Tgui.Windows";

    /// <summary>
    ///     How much a window was given, so that it is given once, and so that a window that sets its height again later (the
    ///         wires window from its wire count, the lathe from its layout) can add the same.
    /// </summary>
    private static readonly AttachedProperty<float> GrownProperty =
        AttachedProperty<float>.Create("KsBaronGrown", typeof(KsBaronPortedWindowUIController), 0f);

    [Dependency] private IConfigurationManager _configurationManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        UIManager.WindowRoot.OnChildAdded += OnWindowAdded;
    }

    private void OnWindowAdded(Control control)
    {
        if (control is not FancyWindow window || _configurationManager.GetCVar(KsCCVars.TguiStyle))
            return;

        Grow(window);
    }

    /// <summary>
    ///     Adds the difference between Baron's header and tgui's title bar to a ported window's stated height.
    /// </summary>
    public static void Grow(FancyWindow window)
    {
        if (window.GetValue(GrownProperty) > 0f || !IsPorted(window) || !float.IsFinite(window.SetHeight))
            return;

        // The header's height under the sheet now in force.
        if (window.ContentsContainer.Parent is not { ChildCount: > 0 } frame)
            return;

        window.ForceRunStyleUpdate();
        var header = frame.GetChild(0);
        header.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
        var extra = header.DesiredSize.Y - KsTguiGeometry.TitleBarHeight;
        if (extra <= 0f)
            return;

        window.SetValue(GrownProperty, extra);
        window.SetHeight += extra;
        if (float.IsFinite(window.MinHeight) && window.MinHeight > 0f)
            window.MinHeight += extra;
    }

    /// <summary>
    ///     What a ported window was grown by under Baron, for one that states its height again after opening. Zero under tgui,
    ///         so tg's sizes are unchanged there.
    /// </summary>
    public static float ExtraHeight(FancyWindow window)
    {
        return window.GetValue(GrownProperty);
    }

    public static bool IsPorted(FancyWindow window)
    {
        return window.GetType().Namespace?.StartsWith(PortedNamespace, StringComparison.Ordinal) == true;
    }
}
