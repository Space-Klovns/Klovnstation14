using Content.Client._KS14.UserInterface.Styles;
using Content.Client.Resources;
using Content.Client.Stylesheets;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._KS14.Lobby.Kharkiv;

/// <summary>
///     Class names used by the "Escape from Klovn" main menu. Registered in <see cref="KsKharkivSheetlet"/>.
/// </summary>
public static class KsKharkivStyleClasses
{
    public const string MenuTitle = "KsKharkivMenuTitle";
    public const string MenuItem = "KsKharkivMenuItem";
    public const string Status = "KsKharkivStatus";
    public const string BannerPanel = "KsKharkivBannerPanel";
    public const string BannerCaption = "KsKharkivBannerCaption";
    public const string BannerValue = "KsKharkivBannerValue";
    public const string BannerDivider = "KsKharkivBannerDivider";
    public const string NavLabel = "KsKharkivNavLabel";
    public const string Bar = "KsKharkivBar";
    public const string NewChip = "KsKharkivNewChip";
    public const string ExpansionsTitle = "KsKharkivExpansionsTitle";
    public const string Version = "KsKharkivVersion";
    public const string ZoneLabel = "KsKharkivZoneLabel";
    public const string SetupBackdrop = "KsKharkivSetupBackdrop";
    public const string Badge = "KsKharkivBadge";
    public const string BadgeGrey = "KsKharkivBadgeGrey";
    public const string BadgeText = "KsKharkivBadgeText";
    public const string CoinText = "KsKharkivCoinText";
}

/// <summary>
///     Styling for the main menu: thin centred menu items, the season banner and the bottom bar. Registered after the
///     theme sheetlets, whose textured <see cref="ContainerButton"/> and <see cref="PanelContainer"/> boxes it overrides
///     through higher selector specificity.
/// </summary>
public sealed class KsKharkivSheetlet : Sheetlet<PalettedStylesheet>
{
    private static readonly Color TextColor = Color.FromHex("#ECECEC");
    private static readonly Color DimColor = Color.FromHex("#9A9A9A");
    private static readonly Color ReadyColor = Color.FromHex("#8FE05A");
    private static readonly Color AmberColor = Color.FromHex("#F0A21E");
    private static readonly Color BannerCaptionColor = Color.FromHex("#9AA6A2");

    public override StyleRule[] GetRules(PalettedStylesheet sheet, object config)
    {
        var rules = new List<StyleRule>();

        // Sheetlets are built by reflection with no injection, so the cvar is resolved here. The stylesheet is
        //      rebuilt by KsLobbyFontUIController when the cvar changes.
        var family = Content.Client._KS14.Lobby.KsLobbySheetlet.ResolveFontFamily(
            IoCManager.Resolve<IConfigurationManager>().GetCVar(KsCCVars.LobbyFont));
        var regular = family.Regular;
        var bold = family.Bold;

        var title = ResCache.GetFont(regular, 44);
        var item = ResCache.GetFont(regular, 34);

        AddMenuButtonRules(rules, KsKharkivStyleClasses.MenuTitle, title, 8, TextColor, pressedColor: ReadyColor);
        AddMenuButtonRules(rules, KsKharkivStyleClasses.MenuItem, item, 4, TextColor, pressedColor: TextColor);

        rules.AddRange([
            E<Label>().Class(KsKharkivStyleClasses.Status).Font(ResCache.GetFont(regular, 15)).FontColor(DimColor),

            // The banner's art is drawn at its real size with a transparent margin for the halo: the box expands into
            //      that margin, and the patch margins keep the corner brackets sharp.
            E<PanelContainer>().Class(KsKharkivStyleClasses.BannerPanel).Panel(BannerBox()),
            E<Label>().Class(KsKharkivStyleClasses.BannerCaption).Font(ResCache.GetFont(regular, 13)).FontColor(BannerCaptionColor),
            E<Label>().Class(KsKharkivStyleClasses.BannerValue).Font(ResCache.GetFont(bold, 21)).FontColor(Color.FromHex("#F2F6F4")),
            E<PanelContainer>().Class(KsKharkivStyleClasses.BannerDivider).Panel(new StyleBoxFlat(Color.White.WithAlpha(0.16f))),

            E<Label>().Class(KsKharkivStyleClasses.NavLabel).Font(ResCache.GetFont(regular, 15)),
            // Edge to edge: the bleed covers the pixel a fractional UI scale can leave off the window's bottom.
            E<PanelContainer>().Class(KsKharkivStyleClasses.Bar).Panel(new KsStyleBoxGradient(
                Color.FromHex("#0E0E0E").WithAlpha(0.82f), Color.FromHex("#0E0E0E").WithAlpha(0.82f), KsGradientDirection.TopToBottom)
            {
                Bleed = new Thickness(0f, 0f, 0f, 2f),
            }),

            E<Label>().Class(KsKharkivStyleClasses.NewChip).Font(ResCache.GetFont(bold, 9)).FontColor(ReadyColor),
            E<Label>().Class(KsKharkivStyleClasses.ExpansionsTitle).Font(ResCache.GetFont(regular, 24)).FontColor(AmberColor),
            E<Label>().Class(KsKharkivStyleClasses.Version).Font(ResCache.GetFont(regular, 14)).FontColor(DimColor),
            E<Label>().Class(KsKharkivStyleClasses.ZoneLabel).Font(ResCache.GetFont(regular, 24)).FontColor(Color.FromHex("#D8D8D8")),

            E<PanelContainer>().Class(KsKharkivStyleClasses.Badge).Panel(BadgeBox(Color.FromHex("#9BE52E"))),
            E<PanelContainer>().Class(KsKharkivStyleClasses.BadgeGrey).Panel(BadgeBox(Color.FromHex("#C9C9BE"))),
            E<Label>().Class(KsKharkivStyleClasses.BadgeText).Font(ResCache.GetFont(bold, 11)).FontColor(Color.FromHex("#101410")),
            E<Label>().Class(KsKharkivStyleClasses.CoinText).Font(ResCache.GetFont(regular, 12)).FontColor(Color.FromHex("#C98A1C")),
            E<PanelContainer>().Class(KsKharkivStyleClasses.SetupBackdrop).Panel(new StyleBoxFlat(Color.Black.WithAlpha(0.8f))),
        ]);

        return rules.ToArray();
    }

    /// <summary>Margin round the banner art's box, in its texture, for the outer halo. Matches BANNER_PAD in the generator.</summary>
    private const float BannerPad = 14f;

    private StyleBoxTexture BannerBox()
    {
        var box = new StyleBoxTexture
        {
            Texture = ResCache.GetTexture("/Textures/_KS14/Lobby/Kharkiv/banner_panel.png"),
            Mode = StyleBoxTexture.StretchMode.Stretch,
        };
        box.SetPatchMargin(StyleBox.Margin.All, BannerPad + 14f);
        box.SetExpandMargin(StyleBox.Margin.All, BannerPad);
        box.SetContentMarginOverride(StyleBox.Margin.All, 0f);
        return box;
    }

    private static StyleBoxFlat BadgeBox(Color fill)
    {
        return new StyleBoxFlat(fill)
        {
            ContentMarginLeftOverride = 4,
            ContentMarginRightOverride = 4,
            ContentMarginTopOverride = 0,
            ContentMarginBottomOverride = 0,
        };
    }

    private static MutableSelectorElement ButtonAt(string ownClass, string pseudo)
    {
        // The button class and a pseudo-class are always included so these rules out-rank the theme's, which have
        //      fewer class selectors.
        return E<ContainerButton>().Class(ContainerButton.StyleClassButton).Pseudo(pseudo).Class(ownClass);
    }

    private static void AddMenuButtonRules(
        List<StyleRule> rules,
        string ownClass,
        Font font,
        float verticalMargin,
        Color normal,
        Color pressedColor)
    {
        (string Pseudo, Color Color, float Underline)[] states =
        [
            (ContainerButton.StylePseudoClassNormal, normal.WithAlpha(0.92f), 0f),
            (ContainerButton.StylePseudoClassHover, Color.White, 0.55f),
            (ContainerButton.StylePseudoClassPressed, pressedColor, 0.55f),
            (ContainerButton.StylePseudoClassDisabled, normal.WithAlpha(0.28f), 0f),
        ];

        foreach (var (pseudo, color, underline) in states)
        {
            // A transparent box with a one-sided border: the hover underline. The margins are the same in every state,
            //      so the item doesn't move when it lights up.
            var box = new StyleBoxFlat(Color.White.WithAlpha(0f))
            {
                BorderColor = color.WithAlpha(underline),
                BorderThickness = new Thickness(0f, 0f, 0f, 2f),
                ContentMarginLeftOverride = 44,
                ContentMarginRightOverride = 44,
                ContentMarginTopOverride = verticalMargin,
                ContentMarginBottomOverride = verticalMargin,
            };

            rules.Add(ButtonAt(ownClass, pseudo)
                .Box(box)
                .Prop(Control.StylePropertyModulateSelf, Color.White));
            rules.Add(ButtonAt(ownClass, pseudo).ParentOf(E<Label>())
                .Font(font)
                .FontColor(color)
                .AlignMode(Label.AlignMode.Center));
        }
    }
}
