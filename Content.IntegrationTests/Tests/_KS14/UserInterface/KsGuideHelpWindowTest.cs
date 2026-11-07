#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client.Guidebook.Components;
using Content.Client.UserInterface.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The header's guidebook button comes from the entity, with nothing asked of the window. This walks the prototypes, one for
///         each bound user interface class, so a new machine is covered without touching the test.
/// </summary>
[TestOf(typeof(Content.Client.KsBoundUserInterfaceExt))]
public sealed class KsGuideHelpWindowTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";

    [TestCase(true)]
    [TestCase(false)]
    public async Task EveryGuidedWindowHasItsGuide(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        var interfaces = typeof(UserInterfaceComponent).GetField("Interfaces", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new System.InvalidOperationException("The test's way of reading a prototype's keys has gone.");

        // A prototype, a key, and the client class that key opens: the first prototype for each class.
        var targets = new List<(EntProtoId Prototype, System.Enum Key, string BuiClass)>();
        var seenClasses = new HashSet<string>();
        await Client.WaitPost(() =>
        {
            var factory = Client.EntMan.ComponentFactory;
            foreach (var prototype in CProtoMan.EnumeratePrototypes<EntityPrototype>().OrderBy(prototype => prototype.ID))
            {
                if (prototype.Abstract
                    || !prototype.TryGetComponent<GuideHelpComponent>(out _, factory)
                    || !prototype.TryGetComponent<UserInterfaceComponent>(out var userInterfaceComponent, factory))
                {
                    continue;
                }

                dynamic keys = interfaces.GetValue(userInterfaceComponent)!;
                foreach (var entry in keys)
                {
                    string buiClass = entry.Value.ClientType;
                    if (seenClasses.Add(buiClass))
                        targets.Add((prototype.ID, (System.Enum)entry.Key, buiClass));
                }
            }
        });

        Assert.That(targets, Has.Count.GreaterThan(10), "The walk should find plenty of machines.");

        // Built here, rather than opened through the server: all this needs is the window, and the server would want every kind
        //      of entity in reach of a mob.
        var open = typeof(BoundUserInterface).GetMethod("Open", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var reflection = Client.ResolveDependency<IReflectionManager>();
        var windowRoot = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot;

        var missing = new List<string>();
        var checkedWindows = 0;
        await Client.WaitPost(() =>
        {
            foreach (var (prototype, key, buiClass) in targets)
            {
                var ownerUid = Client.EntMan.SpawnEntity(prototype, MapCoordinates.Nullspace);
                var bui = (BoundUserInterface)System.Activator.CreateInstance(reflection.LooseGetType(buiClass), ownerUid, key)!;
                var windowsBefore = windowRoot.Children.ToHashSet();
                try
                {
                    open.Invoke(bui, null);
                }
                catch (TargetInvocationException)
                {
                    // Some want a state the test doesn't give them. The window they had made by then still counts.
                }

                foreach (var window in windowRoot.Children.Where(child => !windowsBefore.Contains(child)).OfType<BaseWindow>())
                {
                    checkedWindows++;

                    // A DefaultWindow has its button from the tgui chrome, and a bare BaseWindow has no title bar to put one in.
                    var hasGuide = window switch
                    {
                        FancyWindow fancyWindow => fancyWindow.HelpGuidebookIds != null,
                        DefaultWindow defaultWindow => !tgui || KsTguiChromeUIController.HasHelpButton(defaultWindow),
                        _ => true,
                    };

                    if (!hasGuide)
                        missing.Add($"{prototype} {window.GetType().Name}");
                }

                bui.Dispose();
                Client.EntMan.DeleteEntity(ownerUid);
            }
        });

        Assert.That(checkedWindows, Is.GreaterThan(10), "The walk should have opened plenty of windows.");

        Assert.That(missing, Is.Empty, "These windows have no guide: " + string.Join("; ", missing));
    }

    /// <summary>
    ///     The stock button is a small texture with its own margins. Under tgui it's the same control as the X, in a cell of the
    ///         same size beside it, and it goes back when the theme does.
    /// </summary>
    [Test]
    public async Task AFancyWindowsButtonMatchesTheCloseButton()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        var configuration = Client.ResolveDependency<Robust.Shared.Configuration.IConfigurationManager>();
        FancyWindow window = null!;
        await Client.WaitPost(() =>
        {
            window = new FancyWindow();
            window.OpenCentered();
            window.HelpGuidebookIds = new();
        });
        await Pair.RunTicksSync(3);

        await Client.WaitPost(() =>
        {
            Assert.That(window.HelpButton.Visible, Is.False, "The stock button would show beside the chrome's.");
            var help = window.KsChromeHelpButton as KsTguiTitleBarButton;
            Assert.That(help, Is.Not.Null);
            Assert.That(help!.Visible, Is.True);

            var close = help.Parent!.Children.OfType<KsTguiTitleBarButton>().Single(button => button != help);
            Assert.That(help.Size, Is.EqualTo(close.Size), "The two should be the same size.");
            Assert.That(help.GlobalPosition.X + help.Size.X, Is.EqualTo(close.GlobalPosition.X).Within(0.01f), "They should sit side by side, the help first.");

            window.HelpGuidebookIds = null;
            Assert.That(help.Visible, Is.False, "A window with no guide has no button.");
            window.HelpGuidebookIds = new();

            configuration.SetCVar(KsCCVars.TguiStyle, false);
        });
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            Assert.That(window.KsChromeHelpButton, Is.Null);
            Assert.That(window.HelpButton.Visible, Is.True, "Baron has the stock button.");
            configuration.SetCVar(KsCCVars.TguiStyle, true);
            window.Close();
        });
    }

    [Test]
    public async Task TheDefaultWindowsButtonComesAndGoesWithTheTheme()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        var configuration = Client.ResolveDependency<Robust.Shared.Configuration.IConfigurationManager>();
        DefaultWindow window = null!;
        await Client.WaitPost(() =>
        {
            window = new DefaultWindow();
            window.OpenCentered();
            Client.ResolveDependency<IUserInterfaceManager>().GetUIController<KsTguiChromeUIController>().SetGuides(window, new());

            Assert.That(KsTguiChromeUIController.HasHelpButton(window), Is.True);
            configuration.SetCVar(KsCCVars.TguiStyle, false);
        });
        await Pair.RunTicksSync(2);

        await Client.WaitPost(() =>
        {
            Assert.That(KsTguiChromeUIController.HasChrome(window), Is.False);
            configuration.SetCVar(KsCCVars.TguiStyle, true);
        });
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            Assert.That(KsTguiChromeUIController.HasHelpButton(window), Is.True, "A window under the theme again should have its button again.");
            window.Close();
        });
    }
}
