#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Bwoink;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Tgui;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     tgui's <c>theme="admin"</c>: the palette comes out of <c>themes/admin.scss</c>, the admin-only windows wear it under
///         tgui and are stock under Baron, and a window that regular players also see is left alone. How it looks is for a
///         real client.
/// </summary>
[TestOf(typeof(KsTguiAdminWindows))]
public sealed class KsTguiAdminThemeTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

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

    private Stylesheet AdminSheet()
    {
        return ((StylesheetManager)Client.ResolveDependency<IStylesheetManager>()).KsGetThemedSheet(KsTguiWindowTheme.Admin);
    }

    /// <summary>
    ///     What <c>themes/admin.scss</c> sets, taken through the same derivations as every other theme: base
    ///         <c>hsl(205, 17%, 20%)</c>, <c>--primary-hue: 200</c> (saturation and lightness stay tgui's), a title bar with
    ///         <c>--secondary-saturation: 0</c> and a 1.75 lightness adjustment, a gradient spread of 6, and both stripes set.
    /// </summary>
    [Test]
    public void AdminPaletteComesOutOfItsTokens()
    {
        var admin = KsTguiThemePalette.Admin;

        Assert.Multiple(() =>
        {
            AssertColor(admin.Base, KsTguiColors.FromHsl(205f, 17f, 20f), "base");
            // --secondary-saturation 0 takes the colour out of the title bar, and the adjustment is 1.75 over the base's 20.
            AssertColor(admin.TitleBar, KsTguiColors.FromHsl(205f, 0f, 21.75f), "title bar");
            AssertColor(admin.BaseStart, KsTguiColors.FromHsl(205f, 17f, 26f), "gradient start");
            AssertColor(admin.BaseEnd, KsTguiColors.FromHsl(205f, 17f, 14f), "gradient end");
            AssertColor(admin.PrimaryBase, KsTguiColors.FromHsl(200f, 37.5f, 45f), "primary keeps tgui's saturation and lightness");
            AssertColor(admin.Primary.Element, KsTguiColors.FromHsl(200f, 37.5f, 40f), "button");
            AssertColor(admin.Label, KsTguiColors.FromHsl(200f, 17.5f, 62.5f), "label follows the primary's hue");
            AssertColor(admin.InputBackground, KsTguiColors.FromHsl(205f, 17f, 5f), "input follows the base");
            AssertColor(admin.InputBorder, KsTguiColors.FromHsl(200f, 100f, 75f), "input border follows the primary's hue");
            // Nothing else is set, so a selected button, caution and danger are tgui's own.
            AssertColor(admin.SelectedButton.Element, KsTguiThemePalette.Nanotrasen.SelectedButton.Element, "selected");
            AssertColor(admin.Danger.Element, KsTguiThemePalette.Nanotrasen.Danger.Element, "danger");
            Assert.That(admin.LogoTexture, Is.EqualTo("admin_logo.png"));
        });
    }

    /// <summary>
    ///     Both stripes are set by the admin theme and only the odd one by the others, so the clear even stripe is how a
    ///         theme without one stays as it was.
    /// </summary>
    [Test]
    public void AdminHasBothStripesAndTheOthersOnlyTheOdd()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KsTguiThemePalette.Admin.Candystripe.A, Is.GreaterThan(0f));
            Assert.That(KsTguiThemePalette.Admin.CandystripeEven.A, Is.GreaterThan(0f));
            Assert.That(KsTguiThemePalette.Admin.CandystripeEven, Is.Not.EqualTo(KsTguiThemePalette.Admin.Candystripe));
            Assert.That(KsTguiThemePalette.Nanotrasen.CandystripeEven.A, Is.EqualTo(0f));
            Assert.That(KsTguiThemePalette.Syndicate.CandystripeEven.A, Is.EqualTo(0f));
        });
    }

    /// <summary>
    ///     Every window on the list is a window the chrome handles, and the private ones resolve, so a rename does not
    ///         quietly drop a window from the theme.
    /// </summary>
    [Test]
    public void TheListNamesRealWindows()
    {
        var clientAssembly = typeof(KsTguiAdminWindows).Assembly;

        Assert.Multiple(() =>
        {
            foreach (var type in KsTguiAdminWindows.WindowTypes)
                Assert.That(type.IsAssignableTo(typeof(DefaultWindow)) || type.IsAssignableTo(typeof(FancyWindow)), type.Name + " is a window with tgui chrome");

            foreach (var name in KsTguiAdminWindows.NestedWindowTypeNames)
            {
                var type = clientAssembly.GetType(name);
                Assert.That(type, Is.Not.Null, name + " exists");
                Assert.That(type!.IsAssignableTo(typeof(DefaultWindow)), name + " is a DefaultWindow");
            }
        });
    }

    /// <summary>
    ///     A window of every type on the list that can be made without arguments comes out with the admin sheet under tgui, and
    ///         is back on the UI's own under Baron.
    /// </summary>
    [Test]
    public async Task AdminWindowsWearTheAdminSheetUnderTguiAndNothingUnderBaron()
    {
        var made = new List<BaseWindow>();
        await Client.WaitPost(() =>
        {
            foreach (var type in KsTguiAdminWindows.WindowTypes)
            {
                if (type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                BaseWindow? window = null;
                try
                {
                    window = (BaseWindow)Activator.CreateInstance(type)!;
                    window.OpenCentered();
                    made.Add(window);
                }
                catch (Exception exception) when (exception is TargetInvocationException or NullReferenceException)
                {
                    // One that needs something a bare client does not have (LoadBlueprintsWindow wants the map's data on
                    //      entering the tree) is not the subject here.
                    window?.Dispose();
                }
            }
        });
        await Pair.RunTicksSync(2);

        // Most of the list has to be makeable, or this checks nothing.
        Assert.That(made.Count, Is.GreaterThanOrEqualTo(KsTguiAdminWindows.WindowTypes.Count / 2));

        await Client.WaitAssertion(() =>
        {
            var sheet = AdminSheet();
            Assert.Multiple(() =>
            {
                foreach (var window in made)
                {
                    Assert.That(window.GetKsTheme(), Is.EqualTo(KsTguiWindowTheme.Admin), window.GetType().Name);
                    Assert.That(window.Stylesheet, Is.SameAs(sheet), window.GetType().Name + " sheet");
                }
            });
        });

        await SetTheme(false);

        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var window in made)
                    Assert.That(window.Stylesheet, Is.Null, window.GetType().Name + " under Baron");
            });
        });

        await SetTheme(true);

        await Client.WaitPost(() =>
        {
            foreach (var window in made)
                window.Close();
        });
    }

    /// <summary>
    ///     A window regular players see, a plain <see cref="FancyWindow"/> here, is not given the admin theme.
    /// </summary>
    [Test]
    public async Task APlayerWindowIsNotThemed()
    {
        FancyWindow window = null!;
        await Client.WaitPost(() =>
        {
            window = new FancyWindow();
            window.OpenCentered();
        });
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            Assert.That(window.GetKsTheme(), Is.Null);
            Assert.That(((Control)window).Stylesheet, Is.Null); // FancyWindow.Stylesheet is its own, by name
            Assert.That(KsTguiAdminWindows.IsAdminWindow(window), Is.False);
            window.Close();
        });
    }

    /// <summary>
    ///     The player's own ahelp window is tg's "Create Adminhelp", which is theme="admin", and is a plain window that nothing
    ///         else can pick out, so it is given the theme where it is made.
    /// </summary>
    [Test]
    public async Task ThePlayersAhelpWindowIsAdminThemed()
    {
        var userId = new NetUserId(Guid.NewGuid());
        UserAHelpUIHandler handler = null!;
        await Client.WaitPost(() =>
        {
            handler = new UserAHelpUIHandler(userId);
            handler.Open(userId, relayActive: false);
        });
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            var windowRoot = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot;
            var window = windowRoot.Children.OfType<DefaultWindow>().Single(candidate => candidate.GetKsTheme() == KsTguiWindowTheme.Admin);
            Assert.That(window.Stylesheet, Is.SameAs(AdminSheet()));
            handler.Close();
            handler.Dispose();
        });
    }

    /// <summary>
    ///     The even stripe reaches a candystripe container in an admin window, and the odd one is the theme's.
    /// </summary>
    [Test]
    public async Task AdminWindowStripesBothRows()
    {
        FancyWindow window = null!;
        KsTguiCandystripe stripes = null!;
        await Client.WaitPost(() =>
        {
            window = new FancyWindow { KsTheme = KsTguiWindowTheme.Admin };
            window.ContentsContainer.AddChild(stripes = new KsTguiCandystripe());
            window.OpenCentered();
        });
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            stripes.ForceRunStyleUpdate();
            Assert.That(stripes.TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyCandystripe, out var odd));
            Assert.That(stripes.TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyCandystripeEven, out var even));
            AssertColor(odd, KsTguiThemePalette.Admin.Candystripe, "odd stripe");
            Assert.That(even.A, Is.GreaterThan(0f), "the even stripe is set");
            window.Close();
        });
    }
}
