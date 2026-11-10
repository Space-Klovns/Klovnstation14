using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Client._KS14.Nuke.UI;
using Content.Client._KS14.Tgui;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The tgui toggle, and what its chrome and sheetlet do to real controls. How any of it looks is for a real client: nothing
///         headless draws, so these pin what a wrong rule or a missed restore leaves behind.
/// </summary>
[TestOf(typeof(KsTguiThemeUIController))]
public sealed class KsTguiThemeTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    /// <summary>
    ///     The theme restyles on the frame after the cvar changes.
    /// </summary>
    private async Task SetTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);
    }

    private static StyleBox PanelBox(Control control)
    {
        control.ForceRunStyleUpdate();
        control.TryGetStyleProperty<StyleBox>(PanelContainer.StylePropertyPanel, out var box);
        return box;
    }

    private static List<T> FindAll<T>(Control root) where T : Control
    {
        var found = new List<T>();
        foreach (var child in root.Children)
        {
            if (child is T match)
                found.Add(match);

            found.AddRange(FindAll<T>(child));
        }

        return found;
    }

    private static PanelContainer DefaultHeader(DefaultWindow window)
    {
        return FindAll<PanelContainer>(window).First(panel => panel.HasStyleClass(DefaultWindow.StyleClassWindowHeader));
    }

    private static TextureButton StockCloseButton(BaseWindow window)
    {
        return FindAll<TextureButton>(window).First(button => button.HasStyleClass(DefaultWindow.StyleClassWindowCloseButton));
    }

    [Test]
    public async Task TogglingRebuildsBothSheetsAndMovesTheirHolders()
    {
        var stylesheetManager = Client.ResolveDependency<IStylesheetManager>();
        var uiManager = Client.ResolveDependency<IUserInterfaceManager>();

        Control holder = null;
        Stylesheet oldNanotrasen = null;
        Stylesheet oldSystem = null;
        await Client.WaitPost(() =>
        {
            oldNanotrasen = stylesheetManager.SheetNanotrasen;
            oldSystem = stylesheetManager.SheetSystem;
            holder = new Control { Stylesheet = oldSystem };
            uiManager.PopupRoot.AddChild(holder);
        });

        await SetTheme(false);

        await Client.WaitAssertion(() =>
        {
            Assert.That(stylesheetManager.SheetNanotrasen, Is.Not.SameAs(oldNanotrasen));
            Assert.That(stylesheetManager.SheetSystem, Is.Not.SameAs(oldSystem));
            Assert.That(uiManager.Stylesheet, Is.SameAs(stylesheetManager.SheetNanotrasen));
            Assert.That(holder.Stylesheet, Is.SameAs(stylesheetManager.SheetSystem),
                "A control holding the System sheet keeps the old theme unless it's moved");
            Assert.That(stylesheetManager.TryGetStylesheet("System", out var byName), Is.True);
            Assert.That(byName, Is.SameAs(stylesheetManager.SheetSystem), "FancyWindow.Stylesheet resolves sheets by name");

            holder.Orphan();
        });
    }

    /// <remarks>
    ///     On both sheets, because they aren't alike: ExecutorStyle clears BackgroundPanel's tint on the Nanotrasen sheet
    ///         alone, which would hide the System sheet's windows going dark.
    /// </remarks>
    [Test]
    public async Task SheetletReachesWhatItSaysItDoes([Values(false, true)] bool systemSheet)
    {
        var uiManager = Client.ResolveDependency<IUserInterfaceManager>();
        var stylesheetManager = Client.ResolveDependency<IStylesheetManager>();

        Control root = null;
        Button button = null;
        MenuButton menuButton = null;
        PanelContainer windowBackground = null;
        PanelContainer backgroundPanel = null;
        await Client.WaitPost(() =>
        {
            root = new Control
            {
                Children =
                {
                    (button = new Button()),
                    (menuButton = new MenuButton()),
                    (windowBackground = new PanelContainer
                    {
                        StyleClasses = { StyleClass.BackgroundPanel, KsTguiSheetlet.StyleClassWindowBackground },
                    }),
                    (backgroundPanel = new PanelContainer { StyleClasses = { StyleClass.BackgroundPanel } }),
                },
            };
            if (systemSheet)
                root.Stylesheet = stylesheetManager.SheetSystem;

            uiManager.PopupRoot.AddChild(root);
        });

        await Client.WaitAssertion(() =>
        {
            root.ForceRunStyleUpdate();

            Assert.Multiple(() =>
            {
                Assert.That(button.TryGetStyleProperty<StyleBox>(ContainerButton.StylePropertyStyleBox, out var buttonBox));
                Assert.That(buttonBox, Is.TypeOf<KsTguiStyleBox>());

                // MenuButton never gets the button class, so a rule on that class leaves the top bar stock.
                Assert.That(menuButton.TryGetStyleProperty<StyleBox>(ContainerButton.StylePropertyStyleBox, out var menuBox));
                Assert.That(menuBox, Is.TypeOf<KsTguiStyleBox>());

                Assert.That(PanelBox(windowBackground), Is.TypeOf<KsTguiWindowBackgroundStyleBox>());

                // Upstream tints BackgroundPanel. Left in place, the window goes near black and the logo with it.
                Assert.That(windowBackground.ActualModulateSelf, Is.EqualTo(Color.White));
                Assert.That(backgroundPanel.ActualModulateSelf, Is.EqualTo(Color.White));

                // It fills whole surfaces with nothing behind them, such as the main menu, so it can't be see-through.
                var solid = (KsTguiStyleBox)PanelBox(backgroundPanel);
                Assert.That(solid.BackgroundColor.A, Is.EqualTo(1f));
            });
        });

        await SetTheme(false);

        await Client.WaitAssertion(() =>
        {
            root.ForceRunStyleUpdate();

            Assert.That(button.TryGetStyleProperty<StyleBox>(ContainerButton.StylePropertyStyleBox, out var buttonBox));
            Assert.That(buttonBox, Is.Not.TypeOf<KsTguiStyleBox>(), "Off should leave Baron");

            root.Orphan();
        });
    }

    [Test]
    public async Task ChromeGoesOnAndComesOffBothWindowTypes()
    {
        FancyWindow fancyWindow = null;
        DefaultWindow defaultWindow = null;
        var stockDefaultHeight = 0f;

        await SetTheme(false);
        await Client.WaitPost(() =>
        {
            fancyWindow = new FancyWindow { Title = "air alarm" };
            defaultWindow = new DefaultWindow();
            fancyWindow.OpenCentered();
            defaultWindow.OpenCentered();
        });
        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            Assert.That(KsTguiChromeUIController.HasChrome(fancyWindow), Is.False);
            Assert.That(fancyWindow.KsTitleLabel.Text, Is.EqualTo("air alarm"));
            stockDefaultHeight = DefaultHeader(defaultWindow).MinSize.Y;
        });

        await SetTheme(true);
        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(KsTguiChromeUIController.HasChrome(fancyWindow));
                Assert.That(KsTguiChromeUIController.HasChrome(defaultWindow));

                Assert.That(fancyWindow.KsHeader.Size.Y, Is.EqualTo(KsTguiGeometry.TitleBarHeight).Within(0.5f));
                Assert.That(DefaultHeader(defaultWindow).Size.Y, Is.EqualTo(KsTguiGeometry.TitleBarHeight).Within(0.5f));

                Assert.That(StockCloseButton(fancyWindow).Visible, Is.False);
                // The guidebook's button is there too, hidden on a window with no guide.
                Assert.That(FindAll<KsTguiTitleBarButton>(fancyWindow).Where(button => button.Visible), Has.Exactly(1).Items);
                Assert.That(FindAll<KsTguiTitleBarButton>(defaultWindow), Has.Count.EqualTo(1));

                Assert.That(fancyWindow.KsTitleLabel.Text, Is.EqualTo("Air Alarm"));
                Assert.That(fancyWindow.Title, Is.EqualTo("air alarm"), "Callers should get back the title they set");
            });
        });

        await SetTheme(false);
        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(KsTguiChromeUIController.HasChrome(fancyWindow), Is.False);
                Assert.That(KsTguiChromeUIController.HasChrome(defaultWindow), Is.False);
                Assert.That(StockCloseButton(fancyWindow).Visible, Is.True);
                Assert.That(StockCloseButton(defaultWindow).Visible, Is.True);
                Assert.That(FindAll<KsTguiTitleBarButton>(fancyWindow), Is.Empty);
                Assert.That(FindAll<KsTguiWindowHandle>(defaultWindow), Is.Empty);
                Assert.That(DefaultHeader(defaultWindow).MinSize.Y, Is.EqualTo(stockDefaultHeight));
                Assert.That(fancyWindow.KsTitleLabel.Text, Is.EqualTo("air alarm"));
                Assert.That(defaultWindow.Contents.Margin, Is.EqualTo(new Thickness(10f)));
            });

            fancyWindow.Close();
            defaultWindow.Close();
        });
    }

    /// <summary>
    ///     A toggle only walks open windows, so a closed one has to catch up when it comes back.
    /// </summary>
    [Test]
    public async Task WindowClosedWhileChromedComesBackStock()
    {
        FancyWindow window = null;
        await Client.WaitPost(() =>
        {
            window = new FancyWindow();
            window.OpenCentered();
        });

        await Client.WaitPost(() =>
        {
            Assert.That(KsTguiChromeUIController.HasChrome(window));
            window.Close();
        });

        await SetTheme(false);

        await Client.WaitAssertion(() =>
        {
            window.OpenCentered();

            Assert.That(KsTguiChromeUIController.HasChrome(window), Is.False);
            Assert.That(StockCloseButton(window).Visible, Is.True);

            window.Close();
        });
    }

    [Test]
    public async Task ReopeningDoesNotStackChrome()
    {
        DefaultWindow window = null;
        await Client.WaitPost(() =>
        {
            window = new DefaultWindow();
            window.OpenCentered();
            window.Close();
            window.OpenCentered();
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(FindAll<KsTguiTitleBarButton>(window).Where(button => button.Visible), Has.Exactly(1).Items);
            Assert.That(FindAll<KsTguiWindowHandle>(window), Has.Count.EqualTo(1));

            window.Close();
        });
    }

    /// <summary>
    ///     The contents margin is only tgui's where it was the engine's default. AdminCameraWindow chooses its own.
    /// </summary>
    [Test]
    public async Task ChosenContentsMarginSurvives()
    {
        var chosen = new Thickness(3f);

        DefaultWindow chosenWindow = null;
        DefaultWindow plainWindow = null;
        await Client.WaitPost(() =>
        {
            chosenWindow = new DefaultWindow();
            chosenWindow.Contents.Margin = chosen;
            chosenWindow.OpenCentered();

            plainWindow = new DefaultWindow();
            plainWindow.OpenCentered();
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(chosenWindow.Contents.Margin, Is.EqualTo(chosen));
            Assert.That(plainWindow.Contents.Margin, Is.EqualTo(new Thickness(6f)));
        });

        await SetTheme(false);

        await Client.WaitAssertion(() =>
        {
            Assert.That(chosenWindow.Contents.Margin, Is.EqualTo(chosen));

            chosenWindow.Close();
            plainWindow.Close();
        });
    }

    /// <summary>
    ///     The Nuke UI is a retexture of its own. Everything in it overrides the sheet, and its frame isn't a stock one.
    /// </summary>
    [Test]
    public async Task NukeMenuIsUntouched([Values(true, false)] bool tgui)
    {
        await SetTheme(tgui);

        KsNukeMenu window = null;
        await Client.WaitPost(() =>
        {
            window = new KsNukeMenu();
            window.OpenCentered();
        });
        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            window.ForceRunStyleUpdate();

            Assert.Multiple(() =>
            {
                Assert.That(KsTguiChromeUIController.HasChrome(window), Is.False);
                Assert.That(FindAll<KsTguiTitleBarButton>(window), Is.Empty);

                foreach (var panel in FindAll<PanelContainer>(window))
                    Assert.That(panel.PanelOverride, Is.Not.Null, "Each Nuke panel carries its own box");

                foreach (var button in FindAll<KsNukeKeyButton>(window))
                {
                    Assert.That(button.StyleBoxOverride, Is.Not.Null);
                    Assert.That(button.TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyNormalColor, out _), Is.False,
                        "A Nuke key isn't a tgui button, and mustn't be faded as one");
                }
            });

            window.Close();
        });
    }
}
