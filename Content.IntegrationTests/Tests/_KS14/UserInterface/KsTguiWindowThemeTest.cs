#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.Stylesheets;
using Content.Client.Store.Ui;
using Content.Client.UserInterface.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Tgui;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Content.Shared.UserInterface;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     Per-window tgui themes: what a palette works out from tgui-core's tokens, that a window holding one resolves its
///         colours and one without it doesn't, the toggle, and which store gets which. How any of it looks is for a real
///         client.
/// </summary>
[TestOf(typeof(KsTguiThemePalette))]
public sealed class KsTguiWindowThemeTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId Uplink = "BaseUplinkRadio20TC";
    private static readonly EntProtoId Grimoire = "WizardsGrimoire";
    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId RemoteUplinkStore = "StorePresetRemoteUplink";
    private static readonly EntProtoId PersonalAi = "PersonalAI";
    private static readonly EntProtoId Revenant = "MobRevenant";
    private static readonly EntProtoId Changeling = "MobLing";

    private async Task SetTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);
    }

    private static void AssertColor(Color actual, Color expected, string what, float within = 0.012f)
    {
        Assert.That(actual.R, Is.EqualTo(expected.R).Within(within), what + " red");
        Assert.That(actual.G, Is.EqualTo(expected.G).Within(within), what + " green");
        Assert.That(actual.B, Is.EqualTo(expected.B).Within(within), what + " blue");
    }

    /// <summary>
    ///     The Nanotrasen palette is worked out, not typed in, and has to land on the values that were read off tg for that
    ///         theme. If a derivation is wrong, this is where it shows, and Syndicate and Wizard take the same code.
    /// </summary>
    [Test]
    public void TheDefaultPaletteMatchesTheHandWrittenOne()
    {
        var palette = KsTguiThemePalette.Nanotrasen;

        Assert.Multiple(() =>
        {
            AssertColor(palette.Base, KsTguiPalettes.Base, "base");
            AssertColor(palette.TitleBar, KsTguiPalettes.TitleBar, "title bar (secondary)");
            AssertColor(palette.BaseStart, Color.FromHex("#2B2B2B"), "gradient start");
            AssertColor(palette.BaseEnd, Color.FromHex("#212121"), "gradient end");
            AssertColor(palette.Label, KsTguiPalettes.Label, "label");
            AssertColor(palette.PrimaryBase, KsTguiPalettes.PrimaryBase, "primary");
            AssertColor(palette.Primary.Element, KsTguiPalettes.PrimaryElement, "button");
            AssertColor(palette.Primary.Hovered, KsTguiPalettes.Primary.HoveredElement, "button hovered");
            AssertColor(palette.Primary.Pressed, KsTguiPalettes.Primary.PressedElement, "button pressed");
            AssertColor(palette.Primary.Disabled, KsTguiPalettes.Primary.DisabledElement, "button disabled");
            AssertColor(palette.SelectedButton.Element, KsTguiPalettes.SelectedElement, "selected");
            AssertColor(palette.SelectedButton.Hovered, KsTguiPalettes.SelectedHovered, "selected hovered");
            AssertColor(palette.SelectedButton.Pressed, KsTguiPalettes.Positive.PressedElement, "selected pressed");
            AssertColor(palette.SelectedButton.Disabled, KsTguiPalettes.Positive.DisabledElement, "selected disabled");
            AssertColor(palette.InputBorder, KsTguiPalettes.InputBorder, "input border");
            AssertColor(palette.InputBorderIdle, Color.FromHex("#6392C2"), "input border at rest");
            AssertColor(palette.InputBackground, Color.FromHex("#0D0D0D"), "input");
            AssertColor(palette.TabSelectedText, Color.FromHex("#DCE6EF"), "selected tab", within: 0.02f);
            AssertColor(palette.ScrollThumb, Color.FromHex("#3E3E3E"), "scrollbar");
            AssertColor(palette.TextDim, KsTguiPalettes.TextDim, "dim text");
            AssertColor(palette.TextTitle, KsTguiPalettes.TextTitle, "title text");
            AssertColor(palette.SecondaryPalette.DisabledElement, KsTguiPalettes.Secondary.DisabledElement, "secondary disabled");
            AssertColor(palette.Notice, KsTguiPalettes.Notice, "notice box");
            AssertColor(palette.Caution.Element, KsTguiColors.Element(KsTguiColor.Caution), "caution");
            AssertColor(palette.Danger.Element, KsTguiColors.Element(KsTguiColor.Danger), "danger");
        });
    }

    /// <summary>
    ///     The NoticeBox's oklch step, checked on the other four of tgui's notice colours as well, which the sheet has by hand.
    /// </summary>
    [Test]
    public void OklchStepMatchesTheHandWrittenNoticeColours()
    {
        Assert.Multiple(() =>
        {
            AssertColor(KsTguiThemePalette.OklchScale(KsTguiColors.FromHsl(210f, 65f, 47.5f), 0.825f, 0.75f), KsTguiPalettes.NoticeInfo, "blue");
            AssertColor(KsTguiThemePalette.OklchScale(KsTguiColors.FromHsl(140f, 70f, 40f), 0.825f, 0.75f), KsTguiPalettes.NoticeGood, "green");
            AssertColor(KsTguiThemePalette.OklchScale(KsTguiColors.FromHsl(0f, 70f, 50f), 0.825f, 0.75f), KsTguiPalettes.NoticeBad, "red");
            AssertColor(KsTguiThemePalette.OklchScale(KsTguiColors.FromHsl(25f, 90f, 50f), 0.825f, 0.75f), Color.FromHex("#B6571F"), "orange");
        });
    }

    /// <summary>
    ///     What <c>themes/syndicate.scss</c> and <c>wizard.scss</c> set, taken through the derivations: the values that would be
    ///         wrong first if one were mistyped.
    /// </summary>
    [Test]
    public void TheThemesComeOutOfTheirTokens()
    {
        var syndicate = KsTguiThemePalette.Syndicate;
        var wizard = KsTguiThemePalette.Wizard;

        Assert.Multiple(() =>
        {
            AssertColor(syndicate.Base, KsTguiColors.FromHsl(0f, 96f, 17f), "syndicate base");
            // --secondary-lightness-adjustment 11.5 puts the title bar at lightness 28.5.
            AssertColor(syndicate.TitleBar, KsTguiColors.FromHsl(0f, 96f, 28.5f), "syndicate title bar");
            // --base-gradient-spread 6.
            AssertColor(syndicate.BaseStart, KsTguiColors.FromHsl(0f, 96f, 23f), "syndicate gradient start");
            AssertColor(syndicate.BaseEnd, KsTguiColors.FromHsl(0f, 96f, 11f), "syndicate gradient end");
            AssertColor(syndicate.Primary.Element, KsTguiColors.FromHsl(120f, 34f, 30f), "syndicate button");
            AssertColor(syndicate.SelectedButton.Element, KsTguiColors.FromHsl(0f, 90f, 32.5f), "syndicate selected");
            AssertColor(syndicate.Label, KsTguiColors.FromHsl(120f, 17.5f, 62.5f), "syndicate label follows the primary's hue");
            AssertColor(syndicate.InputBackground, KsTguiColors.FromHsl(0f, 96f, 5f), "syndicate input follows the base's hue");
            Assert.That(syndicate.ProgressBarBackdrop.A, Is.GreaterThan(0f));

            AssertColor(wizard.Base, KsTguiColors.FromHsl(205f, 40f, 20f), "wizard base");
            // --color-secondary is set outright, so the adjustment doesn't apply.
            AssertColor(wizard.TitleBar, KsTguiColors.FromHsl(134f, 71f, 31f), "wizard title bar");
            AssertColor(wizard.InputBorder, KsTguiColors.FromHsl(225f, 25f, 40f), "wizard input border");
            AssertColor(wizard.InputBorderIdle, wizard.InputBorder, "wizard input border at rest has no alpha to flatten");
            AssertColor(wizard.Tooltip, KsTguiColors.FromHsl(134f, 61f, 42f), "wizard tooltip");
            Assert.That(wizard.Tooltip.A, Is.EqualTo(1f), "--tooltip-background is set without the .85");
            Assert.That(KsTguiThemePalette.Nanotrasen.Tooltip.A, Is.LessThan(1f));
        });
    }

    private async Task<(FancyWindow Window, Button Button)> OpenThemedWindow(KsTguiWindowTheme? theme)
    {
        FancyWindow window = null!;
        Button button = null!;
        await Client.WaitPost(() =>
        {
            window = new FancyWindow { KsTheme = theme };
            window.ContentsContainer.AddChild(button = new Button { Text = "x" });
            window.OpenCentered();
        });
        await Pair.RunTicksSync(2);

        return (window, button);
    }

    private static Color ButtonColor(Button button)
    {
        button.ForceRunStyleUpdate();
        Assert.That(button.TryGetStyleProperty<Color>(Control.StylePropertyModulateSelf, out var color));
        return color;
    }

    /// <summary>
    ///     A themed window's button comes out in the theme's colour and its neighbour's doesn't, and switching tgui off hands
    ///         the themed window back to Baron.
    /// </summary>
    [Test]
    public async Task ButtonsFollowTheirWindowsTheme()
    {
        var (themed, themedButton) = await OpenThemedWindow(KsTguiWindowTheme.Syndicate);
        var (plain, plainButton) = await OpenThemedWindow(theme: null);

        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                AssertColor(ButtonColor(themedButton), KsTguiThemePalette.Syndicate.Primary.Element, "themed button");
                AssertColor(ButtonColor(plainButton), KsTguiThemePalette.Nanotrasen.Primary.Element, "plain button");
                Assert.That(((Control)themed).Stylesheet, Is.Not.Null); // FancyWindow.Stylesheet is its own, by name
            });
        });

        await SetTheme(false);

        await Client.WaitAssertion(() =>
        {
            Assert.That(((Control)themed).Stylesheet, Is.Null, "Baron has no themes, so the window goes back to the UI's sheet");
            Assert.That(themedButton.TryGetStyleProperty<StyleBox>(ContainerButton.StylePropertyStyleBox, out var box));
            Assert.That(box, Is.Not.TypeOf<KsTguiStyleBox>());
        });

        await SetTheme(true);

        await Client.WaitAssertion(() =>
        {
            AssertColor(ButtonColor(themedButton), KsTguiThemePalette.Syndicate.Primary.Element, "themed button, back on");

            themed.Close();
            plain.Close();
        });
    }

    /// <summary>
    ///     The window's chrome and body are rules of the sheet too, so they are the theme's. Not what they draw, which no
    ///         headless client can see, but which box carries which colours.
    /// </summary>
    [Test]
    public async Task WindowChromeFollowsTheTheme()
    {
        var (window, _) = await OpenThemedWindow(KsTguiWindowTheme.Wizard);

        await Client.WaitAssertion(() =>
        {
            window.KsHeader.ForceRunStyleUpdate();
            Assert.That(window.KsHeader.TryGetStyleProperty<StyleBox>(PanelContainer.StylePropertyPanel, out var box));
            Assert.That(box, Is.TypeOf<KsTguiTitleBarStyleBox>());
            AssertColor(((KsTguiTitleBarStyleBox)box).BackgroundColor, KsTguiThemePalette.Wizard.TitleBar, "title bar");

            window.Close();
        });
    }

    /// <summary>
    ///     A themed sheet is a whole extra stylesheet, so it is built when first asked for and not for every window.
    /// </summary>
    [Test]
    public async Task ThemedSheetIsBuiltOnceUntilTheSheetsAreRebuilt()
    {
        var stylesheetManager = (StylesheetManager)Client.ResolveDependency<IStylesheetManager>();

        Stylesheet first = null!;
        await Client.WaitPost(() =>
        {
            first = stylesheetManager.KsGetThemedSheet(KsTguiWindowTheme.Syndicate);
            Assert.That(stylesheetManager.KsGetThemedSheet(KsTguiWindowTheme.Syndicate), Is.SameAs(first));
            Assert.That(stylesheetManager.KsGetThemedSheet(KsTguiWindowTheme.Wizard), Is.Not.SameAs(first));
            Assert.That(first, Is.Not.SameAs(stylesheetManager.SheetNanotrasen));
        });
    }

    [Test]
    public async Task TheStoresGetTheirThemes()
    {
        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(StoreTheme(Uplink), Is.EqualTo(KsTguiWindowTheme.Syndicate), "an uplink");
                Assert.That(StoreTheme(RemoteUplinkStore), Is.EqualTo(KsTguiWindowTheme.Syndicate), "the uplink in a PDA");
                Assert.That(StoreTheme(Grimoire), Is.EqualTo(KsTguiWindowTheme.Wizard), "the grimoire");
                Assert.That(StoreTheme(PersonalAi), Is.EqualTo(KsTguiWindowTheme.Nanotrasen), "a pAI");
                Assert.That(StoreTheme(Revenant), Is.EqualTo(KsTguiWindowTheme.Syndicate), "a revenant");
                Assert.That(StoreTheme(Changeling), Is.EqualTo(KsTguiWindowTheme.Syndicate), "a changeling");
            });

            // A partial that merged in its theme must still be the upstream prototype.
            Assert.That(CProtoMan.Index(Grimoire).TryGetComponent<StoreComponent>(out var grimoire, CEntMan.ComponentFactory));
            Assert.That(grimoire!.Categories, Is.Not.Empty, "the spellbook's listings survived the partial");
            Assert.That(grimoire.CurrencyWhitelist, Is.Not.Empty);
        });
    }

    private KsTguiWindowTheme StoreTheme(EntProtoId id)
    {
        var prototype = CProtoMan.Index(id);
        Assert.That(prototype.TryGetComponent<StoreComponent>(out var store, CEntMan.ComponentFactory), id + " has a store");
        return store!.KsUiTheme;
    }

    /// <summary>
    ///     The store's BUI hands its window the store's theme, and the PDA's own window is not touched by it.
    /// </summary>
    [TestCase("BaseUplinkRadio20TC", KsTguiWindowTheme.Syndicate)]
    [TestCase("WizardsGrimoire", KsTguiWindowTheme.Wizard)]
    public async Task TheStoreWindowTakesTheStoresTheme(string prototypeId, KsTguiWindowTheme expected)
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid store = default;
        EntityUid user = default;
        await Server.WaitPost(() =>
        {
            store = SEntMan.SpawnEntity(prototypeId, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            user = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 1.5f)));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, user);
            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(store, StoreUiKey.Key, user);
        });
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var uiManager = Client.ResolveDependency<IUserInterfaceManager>();
            var menu = uiManager.WindowRoot.Children.OfType<StoreMenu>().Single();
            Assert.That(menu.GetKsTheme(), Is.EqualTo(expected));

            var sheet = ((StylesheetManager)Client.ResolveDependency<IStylesheetManager>()).KsGetThemedSheet(expected);
            Assert.That(menu.Stylesheet, Is.SameAs(sheet));
        });
    }
}
