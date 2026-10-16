using Content.Client.Resources;
using Content.Client._KS14.UserInterface.Styles;
using Content.Client.Stylesheets;
using Content.Client.Stylesheets.Fonts;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.Shared._KS14.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._KS14.Lobby;

/// <summary>
///     Class names used by the tactical lobby layout. Registered in <see cref="KsLobbySheetlet"/>.
/// </summary>
public static class KsLobbyStyleClasses
{
    public const string KsLobbyMenuButton = "KsLobbyMenuButton";
    public const string KsLobbyMenuButtonSmall = "KsLobbyMenuButtonSmall";
    public const string KsLobbyBottomBar = "KsLobbyBottomBar";
    public const string KsLobbyGlass = "KsLobbyGlass";
    public const string KsLobbyHairline = "KsLobbyHairline";
    public const string KsLobbyTitle = "KsLobbyTitle";
    public const string KsLobbySubtitle = "KsLobbySubtitle";
    public const string KsLobbyCountdown = "KsLobbyCountdown";
    public const string KsLobbyCharName = "KsLobbyCharName";
    public const string KsLobbyCharJob = "KsLobbyCharJob";
    public const string KsLobbyFadeLeft = "KsLobbyFadeLeft";
    public const string KsLobbyFadeBottom = "KsLobbyFadeBottom";
    public const string KsLobbyFadeRight = "KsLobbyFadeRight";
    public const string KsLobbyRichText = "KsLobbyRichText";
}

/// <summary>One selectable lobby font: resource paths for its faces. <paramref name="Black"/> is null when absent.</summary>
public sealed record KsLobbyFontFamily(string Regular, string Bold, string? Black);

/// <summary>
///     Flat, translucent "tactical menu" styling for the lobby. Must be registered after the Baron sheetlet,
///     whose textured <see cref="ContainerButton"/> box it overrides through higher selector specificity.
/// </summary>
public sealed class KsLobbySheetlet : Sheetlet<PalettedStylesheet>
{
    private static readonly Color TextColor = Color.FromHex("#E6E6E6");
    private static readonly Color DimColor = Color.FromHex("#9A9A9A");
    public static readonly Color AccentColor = Color.FromHex("#D9A441"); // also the countdown's base colour in LobbyGui
    private static readonly Color NegativeColor = Color.FromHex("#D9534F");
    private static readonly Color PositiveColor = Color.FromHex("#7BD15A");
    private static readonly Color RichTextColor = Color.FromHex("#BDBDBD");
    private static readonly Color GlassColor = Color.Black.WithAlpha(0.45f);
    private static readonly Color HairlineColor = Color.White.WithAlpha(0.14f);

    /// <summary>
    ///     Gap between small buttons in a row. Rows not styled through the class set this as their SeparationOverride.
    /// </summary>
    public const int SmallButtonSeparation = 6;

    /// <summary>
    ///     Selectable lobby fonts, by the id stored in <see cref="KsCCVars.LobbyFont"/>. Paths live here and nowhere else.
    ///     A family without a black face uses its bold one for the big countdown.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, KsLobbyFontFamily> FontFamilies =
        new Dictionary<string, KsLobbyFontFamily>
        {
            ["bender"] = new("/Fonts/_KS14/Bender/Bender-Regular.otf", "/Fonts/_KS14/Bender/Bender-Bold.otf", "/Fonts/_KS14/Bender/Bender-Black.otf"),
            ["jura"] = new("/Fonts/_KS14/Jura/Jura-Regular.ttf", "/Fonts/_KS14/Jura/Jura-Bold.ttf", null),
        };

    public const string DefaultFontId = "jura";

    /// <summary>Resolves a cvar value to a family, falling back to <see cref="DefaultFontId"/> for an unknown id.</summary>
    public static KsLobbyFontFamily ResolveFontFamily(string? id)
    {
        return id != null && FontFamilies.TryGetValue(id, out var family) ? family : FontFamilies[DefaultFontId];
    }

    public override StyleRule[] GetRules(PalettedStylesheet sheet, object config)
    {
        var rules = new List<StyleRule>();

        // Sheetlets are built by reflection with no injection, so the cvar is resolved here. The stylesheet is
        //      rebuilt by KsLobbyFontUIController when the cvar changes.
        var fontFamily = ResolveFontFamily(IoCManager.Resolve<IConfigurationManager>().GetCVar(KsCCVars.LobbyFont));
        var regularFontPath = fontFamily.Regular;
        var boldFontPath = fontFamily.Bold;
        var blackFontPath = fontFamily.Black ?? fontFamily.Bold;

        var menuFont = ResCache.GetFont(regularFontPath, 26);
        var smallFont = ResCache.GetFont(regularFontPath, 13);

        // Large menu items: text colour, hover/pressed accent.
        AddButtonRules(rules, KsLobbyStyleClasses.KsLobbyMenuButton, parentClass: null, menuFont,
            TextColor, 0f, 4f, disabledColor: TextColor.WithAlpha(0.28f)); // clearly unavailable, e.g. Observe before the round
        // Small utility buttons: dim text, hover accent.
        AddButtonRules(rules, KsLobbyStyleClasses.KsLobbyMenuButtonSmall, parentClass: null, smallFont,
            DimColor, 8f, 3f, chip: true);
        // Buttons that are direct children of a KsLobbyMenuButtonSmall container get the small style too.
        AddButtonRules(rules, ownClass: null, parentClass: KsLobbyStyleClasses.KsLobbyMenuButtonSmall, smallFont,
            DimColor, 8f, 3f, chip: true);
        // Rows of small buttons are spaced so the chips read as separate.
        rules.Add(E<BoxContainer>().Class(KsLobbyStyleClasses.KsLobbyMenuButtonSmall)
            .Prop(BoxContainer.StylePropertySeparation, SmallButtonSeparation));

        // AHelpUIController adds StyleClass.Negative to flag an unread message.
        AddNegativeRules(rules, KsLobbyStyleClasses.KsLobbyMenuButtonSmall, parentClass: null);
        AddNegativeRules(rules, ownClass: null, parentClass: KsLobbyStyleClasses.KsLobbyMenuButtonSmall);
        // ChangelogButton adds StyleClass.Positive when there are unread entries.
        AddPositiveRules(rules, KsLobbyStyleClasses.KsLobbyMenuButtonSmall, parentClass: null);
        AddPositiveRules(rules, ownClass: null, parentClass: KsLobbyStyleClasses.KsLobbyMenuButtonSmall);

        var regular14 = ResCache.GetFont(regularFontPath, 14);
        var regular16 = ResCache.GetFont(regularFontPath, 16);
        var bold30 = ResCache.GetFont(boldFontPath, 30);
        var bold34 = ResCache.GetFont(boldFontPath, 34);
        var countdownFont = ResCache.GetFont(blackFontPath, 44);
        var regular13 = ResCache.GetFont(regularFontPath, 13);

        var glassBox = new StyleBoxFlat(GlassColor)
        {
            ContentMarginLeftOverride = 8,
            ContentMarginTopOverride = 8,
            ContentMarginRightOverride = 8,
            ContentMarginBottomOverride = 8,
        };

        rules.AddRange([
            E<PanelContainer>().Class(KsLobbyStyleClasses.KsLobbyGlass).Panel(glassBox),
            // A flat gradient that bleeds past the screen edge: PixelPosition and PixelSize are truncated separately
            //      (Control.Layout.cs), so at fractional UI scales the bar can otherwise stop a pixel short.
            E<PanelContainer>().Class(KsLobbyStyleClasses.KsLobbyBottomBar).Panel(new KsStyleBoxGradient(
                Color.Black.WithAlpha(0.85f), Color.Black.WithAlpha(0.85f), KsGradientDirection.TopToBottom)
            {
                Bleed = new Thickness(0f, 0f, 0f, 2f),
            }),
            E<PanelContainer>().Class(KsLobbyStyleClasses.KsLobbyHairline).Panel(new StyleBoxFlat(HairlineColor)),

            E<Label>().Class(KsLobbyStyleClasses.KsLobbyTitle).Font(bold34).FontColor(TextColor),
            E<Label>().Class(KsLobbyStyleClasses.KsLobbySubtitle).Font(regular14).FontColor(DimColor),
            E<Label>().Class(KsLobbyStyleClasses.KsLobbyCountdown).Font(countdownFont).FontColor(AccentColor),
            // Baron forces its own font on every RichTextLabel; the class (more specific) swaps it for the lobby font. The
            //      parent form covers a label created in code as a child of a classed container (ServerInfo).
            E<RichTextLabel>().Class(KsLobbyStyleClasses.KsLobbyRichText).Font(regular13)
                .Prop(Control.StylePropertyModulateSelf, RichTextColor),
            E().Class(KsLobbyStyleClasses.KsLobbyRichText).ParentOf(E<RichTextLabel>()).Font(regular13)
                .Prop(Control.StylePropertyModulateSelf, RichTextColor),
            E<Label>().Class(KsLobbyStyleClasses.KsLobbyCharName).Font(bold30).FontColor(TextColor),
            E<Label>().Class(KsLobbyStyleClasses.KsLobbyCharJob).Font(regular16).FontColor(AccentColor),

            E<PanelContainer>().Class(KsLobbyStyleClasses.KsLobbyFadeLeft).Panel(new KsStyleBoxGradient(
                KsGradientDirection.LeftToRight,
                (0f, Color.Black.WithAlpha(0.75f)), (0.55f, Color.Black.WithAlpha(0f)), (1f, Color.Black.WithAlpha(0f)))),
            E<PanelContainer>().Class(KsLobbyStyleClasses.KsLobbyFadeBottom).Panel(new KsStyleBoxGradient(
                Color.Black.WithAlpha(0f), Color.Black.WithAlpha(0.6f), KsGradientDirection.TopToBottom)),
            E<PanelContainer>().Class(KsLobbyStyleClasses.KsLobbyFadeRight).Panel(new KsStyleBoxGradient(
                Color.Black.WithAlpha(0f), Color.Black.WithAlpha(0.55f), KsGradientDirection.LeftToRight)),
        ]);

        return rules.ToArray();
    }

    /// <summary>
    ///     Builds the button selector. The button class and a pseudo-class are always included so these rules
    ///     out-rank Baron's and the default palette modulation, which have fewer class selectors.
    /// </summary>
    private static MutableSelectorElement Button(string? ownClass, string pseudo)
    {
        var button = E<ContainerButton>().Class(ContainerButton.StyleClassButton).Pseudo(pseudo);
        return ownClass == null ? button : button.Class(ownClass);
    }

    private static MutableSelector Root(MutableSelectorElement button, string? parentClass)
    {
        return parentClass == null ? button : E().Class(parentClass).ParentOf(button);
    }

    private static MutableSelector LabelOf(MutableSelectorElement button, string? parentClass)
    {
        return Root(button, parentClass).ParentOf(E<Label>());
    }

    private static void AddButtonRules(
        List<StyleRule> rules,
        string? ownClass,
        string? parentClass,
        Font font,
        Color normal,
        float marginHorizontal,
        float marginVertical,
        bool chip = false,
        Color? disabledColor = null)
    {
        // Chip fill and outline alpha per state: a faint white box, so adjacent small buttons read as separate.
        (string Pseudo, Color Color, float Fill, float Border)[] states =
        [
            (ContainerButton.StylePseudoClassNormal, normal, 0.04f, 0.10f),
            (ContainerButton.StylePseudoClassHover, AccentColor, 0.08f, 0.18f),
            (ContainerButton.StylePseudoClassPressed, AccentColor, 0.12f, 0.22f),
            (ContainerButton.StylePseudoClassDisabled, disabledColor ?? DimColor.WithAlpha(0.5f), 0.02f, 0.05f),
        ];

        foreach (var (pseudo, color, fill, border) in states)
        {
            StyleBox box = chip
                ? new StyleBoxFlat(Color.White.WithAlpha(fill))
                {
                    BorderColor = Color.White.WithAlpha(border),
                    BorderThickness = new Thickness(1),
                }
                : new StyleBoxEmpty();
            box.ContentMarginLeftOverride = marginHorizontal;
            box.ContentMarginRightOverride = marginHorizontal;
            box.ContentMarginTopOverride = marginVertical;
            box.ContentMarginBottomOverride = marginVertical;

            rules.Add(Root(Button(ownClass, pseudo), parentClass)
                .Box(box)
                .Prop(Control.StylePropertyModulateSelf, Color.White));
            var label = LabelOf(Button(ownClass, pseudo), parentClass)
                .Font(font)
                .FontColor(color);
            // Menu items read as a left-aligned list, and ButtonSheetlet centres button labels. Pinning the text to
            //      the left edge also keeps it still when the button's width lags a text change: the engine skips
            //      re-arranging a button whose label re-measures while the button is itself arranging, so READY or
            //      JOIN would otherwise sit centred in NOT READY's old width until the next change.
            if (!chip)
                label.AlignMode(Label.AlignMode.Left);
            rules.Add(label);
        }
    }

    private static void AddNegativeRules(List<StyleRule> rules, string? ownClass, string? parentClass)
    {
        (string Pseudo, Color Color)[] states =
        [
            (ContainerButton.StylePseudoClassNormal, NegativeColor),
            (ContainerButton.StylePseudoClassHover, NegativeColor),
            (ContainerButton.StylePseudoClassPressed, NegativeColor),
            (ContainerButton.StylePseudoClassDisabled, NegativeColor.WithAlpha(0.5f)),
        ];

        foreach (var (pseudo, color) in states)
        {
            rules.Add(LabelOf(Button(ownClass, pseudo).Class(StyleClass.Negative), parentClass).FontColor(color));
        }
    }

    /// <summary>
    ///     Green label plus a faint green highlight box, so an unread changelog reads like a notification badge.
    ///     Same selector shape as the plain rules plus the Positive class, so it out-ranks them.
    /// </summary>
    private static void AddPositiveRules(List<StyleRule> rules, string? ownClass, string? parentClass)
    {
        (string Pseudo, Color Color)[] states =
        [
            (ContainerButton.StylePseudoClassNormal, PositiveColor),
            (ContainerButton.StylePseudoClassHover, PositiveColor),
            (ContainerButton.StylePseudoClassPressed, PositiveColor),
            (ContainerButton.StylePseudoClassDisabled, PositiveColor.WithAlpha(0.5f)),
        ];

        foreach (var (pseudo, color) in states)
        {
            var box = new StyleBoxFlat(PositiveColor.WithAlpha(0.18f))
            {
                BorderColor = PositiveColor.WithAlpha(0.4f),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 3,
                ContentMarginBottomOverride = 3,
            };

            var button = Button(ownClass, pseudo).Class(StyleClass.Positive);
            rules.Add(Root(button, parentClass)
                .Box(box)
                .Prop(Control.StylePropertyModulateSelf, Color.White));
            rules.Add(LabelOf(Button(ownClass, pseudo).Class(StyleClass.Positive), parentClass).FontColor(color));
        }
    }
}
