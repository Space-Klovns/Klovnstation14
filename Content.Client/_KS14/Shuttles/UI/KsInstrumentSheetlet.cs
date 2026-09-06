using Content.Client.Resources;
using Content.Client.Stylesheets;
using Content.Shared._KS14.Sensors.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._KS14.Shuttles.UI;

/// <summary>
///     Type ramp and chrome for the KS instrument shell (<see cref="KsInstrumentWindow"/>
///         and its screens). Discovered by the stylesheet manager via
///         <see cref="CommonSheetletAttribute"/>. The chrome colours come from
///         <see cref="KsInstrumentChrome"/> on the sensor HUD prototype: prototypes are
///         loaded and resolved before EntryPoint.PostInit builds the stylesheets, so
///         indexing here is safe, but the result is session-fixed (GetRules runs once
///         per stylesheet, never on prototype hot-reload). Screen-varying tones stay
///         out of here: each screen tints its labels from its <c>KsInstrumentPalette</c>
///         at runtime.
/// </summary>
[CommonSheetlet]
public sealed class KsInstrumentSheetlet : Sheetlet<PalettedStylesheet>
{
    /// <summary>
    ///     The instrument face: VT323 (OFL), a single-weight CRT-terminal pixel monospace.
    ///         Every manual-draw control in the shell (<see cref="KsInstrumentPanel"/>,
    ///         <see cref="KsBearingPlot"/>, <see cref="KsRadarControl"/>) resolves the same
    ///         path/sizes so the whole shell re-fonts from this one spot. VT323 has no bold
    ///         cut: emphasis is size and palette colour, never a weight change.
    /// </summary>
    public const string FontPath = "/Fonts/_KS14/VT323/VT323-Regular.ttf";

    /// <summary>Body size: readouts, panel titles, plot labels (matches old RobotoMono 10 cap height).</summary>
    public const int FontSizeBody = 13;

    /// <summary>Fine-print size: log rows, hints, the status strip.</summary>
    public const int FontSizeSmall = 10;

    /// <summary>Tab-strip and title-bar size.</summary>
    public const int FontSizeTab = 15;

    /// <summary>Standard readout text (VT323 13).</summary>
    public const string StyleClassText = "KsInstrumentText";

    /// <summary>Panel headings and emphasized values (VT323 13; emphasis is colour, not weight).</summary>
    public const string StyleClassStrong = "KsInstrumentStrong";

    /// <summary>Fine print: log rows, hints, status strip (VT323 10).</summary>
    public const string StyleClassSmall = "KsInstrumentSmall";

    /// <summary>The window title-bar text (VT323 15).</summary>
    public const string StyleClassTitle = "KsInstrumentTitle";

    /// <summary>Shell tab buttons (RADAR // MAP // ...).</summary>
    public const string StyleClassTab = "KsInstrumentTab";

    /// <summary>Screen action buttons (FOCUS / CEASE) and roster rows.</summary>
    public const string StyleClassAction = "KsInstrumentAction";

    /// <summary>
    ///     Strips the vanilla "button" style class a stock <see cref="Button"/> adds
    ///         in its constructor. The NanoTrasen button rules (textured box,
    ///         per-state tints, faded disabled label) select on that class at the same
    ///         or higher specificity as the instrument rules, so a chrome button
    ///         wearing both classes renders vanilla. Must be called on every button
    ///         that wears <see cref="StyleClassTab"/> or <see cref="StyleClassAction"/>.
    /// </summary>
    public static void MakeInstrument(params ContainerButton[] buttons)
    {
        foreach (var button in buttons)
            button.RemoveStyleClass(ContainerButton.StyleClassButton);
    }

    private static StyleBoxFlat ChromeBox(Color background, Color border, int marginH, int marginV)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = marginH,
            ContentMarginRightOverride = marginH,
            ContentMarginTopOverride = marginV,
            ContentMarginBottomOverride = marginV,
        };
    }

    public override StyleRule[] GetRules(PalettedStylesheet sheet, object config)
    {
        // TryIndex only guards a fork shipping without the yaml singleton (then the
        // compiled-in defaults apply); prototypes otherwise resolve strictly before
        // StylesheetManager.Initialize runs in PostInit.
        var chrome = IoCManager.Resolve<IPrototypeManager>()
            .TryIndex(KsSensorHudPrototype.DefaultId, out var hud)
            ? hud.Chrome
            : new KsInstrumentChrome();

        var small = ResCache.GetFont(FontPath, size: FontSizeSmall);
        var body = ResCache.GetFont(FontPath, size: FontSizeBody);
        var tab = ResCache.GetFont(FontPath, size: FontSizeTab);

        var tabBox = ChromeBox(chrome.TabBackground, chrome.TabBorder, 14, 4);
        var tabBoxPressed = ChromeBox(chrome.TabPressedBackground, chrome.TabPressedBorder, 14, 4);
        var tabBoxDisabled = ChromeBox(chrome.TabDisabledBackground, chrome.TabDisabledBorder, 14, 4);

        var actionBox = ChromeBox(chrome.ActionBackground, chrome.ActionBorder, 8, 2);
        var actionBoxPressed = ChromeBox(chrome.ActionPressedBackground, chrome.ActionPressedBorder, 8, 2);
        var actionBoxDisabled = ChromeBox(chrome.ActionDisabledBackground, chrome.ActionDisabledBorder, 8, 2);

        return
        [
            E<Label>().Class(StyleClassText).Font(body),
            E<Label>().Class(StyleClassStrong).Font(body),
            E<Label>().Class(StyleClassSmall).Font(small),
            E<Label>().Class(StyleClassTitle).Font(tab),

            // Tab brightness tiers: pressed > unpressed > disabled.
            // Hover borrows the pressed box so mousing reads as "this would light
            // up"; a disabled tab never hovers because the disabled draw mode is
            // exclusive in ContainerButton.
            E<ContainerButton>().Class(StyleClassTab).Box(tabBox),
            E<ContainerButton>().Class(StyleClassTab).PseudoPressed().Box(tabBoxPressed),
            E<ContainerButton>().Class(StyleClassTab).PseudoHovered().Box(tabBoxPressed),
            E<ContainerButton>().Class(StyleClassTab).PseudoDisabled().Box(tabBoxDisabled),
            E<ContainerButton>().Class(StyleClassTab).ParentOf(E<Label>()).Font(tab),
            E<ContainerButton>().Class(StyleClassTab).ParentOf(E<Label>()).FontColor(chrome.TabText),
            E<ContainerButton>().Class(StyleClassTab).PseudoPressed().ParentOf(E<Label>()).FontColor(chrome.TabPressedText),
            E<ContainerButton>().Class(StyleClassTab).PseudoDisabled().ParentOf(E<Label>()).FontColor(chrome.TabDisabledText),

            E<ContainerButton>().Class(StyleClassAction).Box(actionBox),
            E<ContainerButton>().Class(StyleClassAction).PseudoPressed().Box(actionBoxPressed),
            E<ContainerButton>().Class(StyleClassAction).PseudoHovered().Box(actionBoxPressed),
            E<ContainerButton>().Class(StyleClassAction).PseudoDisabled().Box(actionBoxDisabled),
            E<ContainerButton>().Class(StyleClassAction).ParentOf(E<Label>()).Font(body),
            E<ContainerButton>().Class(StyleClassAction).PseudoDisabled().ParentOf(E<Label>()).FontColor(chrome.ActionDisabledText),
        ];
    }
}
