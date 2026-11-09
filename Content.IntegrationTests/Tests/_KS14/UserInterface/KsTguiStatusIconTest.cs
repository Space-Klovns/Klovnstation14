#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Windows.Lathe;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.Ghost;
using Content.Shared.Lathe;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using Robust.Client.Input;
using Robust.Shared.Maths;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     tg's title bar status icon (layouts/TitleBar.tsx): a green eye for a window that is not an interface of
///         anything (every tgui window has a status), and for a bound user interface an eye that is green while the user can use it, orange for a ghost that can
///         only look, and a red slashed eye while the server would refuse everything.
/// </summary>
[TestOf(typeof(KsTguiTitleStatusIcon))]
public sealed class KsTguiStatusIconTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId LatheProto = "Autolathe";
    private static readonly EntProtoId Human = "MobHuman";

    private async Task<(EntityUid Lathe, EntityUid User)> OpenLathe()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid lathe = default;
        EntityUid user = default;
        await Server.WaitPost(() =>
        {
            lathe = SEntMan.SpawnEntity(LatheProto, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            user = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 1.5f)));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, user);
            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(lathe, LatheUiKey.Key, user);
        });
        await Pair.RunTicksSync(15);
        return (lathe, user);
    }

    private KsTguiLatheWindow LatheWindow()
    {
        return Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<KsTguiLatheWindow>().Single();
    }

    private static KsTguiTitleStatusIcon Icon(BaseWindow window)
    {
        var icon = KsTguiChromeUIController.GetStatusIcon(window);
        Assert.That(icon, Is.Not.Null, "The window should have the status icon.");
        return icon!;
    }

    private static void AssertShows(BaseWindow window, string glyph, Color color, string why)
    {
        var icon = Icon(window);
        Assert.That(icon.Glyph, Is.EqualTo(glyph), why);
        Assert.That(icon.Current, Is.EqualTo(color), why);
    }

    private async Task SetMobState(EntityUid user, MobState state)
    {
        await Server.WaitPost(() => SEntMan.System<MobStateSystem>().ChangeMobState(user, state));

        // Longer than the poll, and than the colour's half second.
        await Pair.RunTicksSync(90);
    }

    [Test]
    public async Task AWindowThatIsNoInterfaceShowsTheInteractiveEye()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            var window = new DefaultWindow { Title = "Plain" };
            window.OpenCentered();

            AssertShows(window, KsTguiIcons.Eye, KsTguiPalettes.Good, "Not a bound interface: the interactive eye, placed with no transition.");
            window.Close();
        });
    }

    [Test]
    public async Task AnInterfaceShowsAGreenEyeAndARedSlashedOneWhileItsUserCantUseIt()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        var (_, user) = await OpenLathe();

        await Client.WaitAssertion(() => AssertShows(LatheWindow(), KsTguiIcons.Eye, KsTguiPalettes.Good, "Alive: a green eye."));

        await SetMobState(user, MobState.Dead);
        await Client.WaitAssertion(() => AssertShows(LatheWindow(), KsTguiIcons.EyeSlash, KsTguiPalettes.Bad, "Dead: a red slashed eye."));
        await Client.WaitAssertion(() =>
            Assert.That(LatheWindow().Children.OfType<KsTguiWindowDimmer>().Single().Visible, Is.True, "The eye is slashed exactly when the window is dimmed."));

        await SetMobState(user, MobState.Alive);
        await Client.WaitAssertion(() => AssertShows(LatheWindow(), KsTguiIcons.Eye, KsTguiPalettes.Good, "Recovered: green again."));

        await Server.WaitPost(() => SEntMan.System<SharedStunSystem>().TryAddStunDuration(user, TimeSpan.FromSeconds(60)));
        await Pair.RunTicksSync(90);
        await Client.WaitAssertion(() => AssertShows(LatheWindow(), KsTguiIcons.EyeSlash, KsTguiPalettes.Bad, "Stunned: a red slashed eye."));
    }

    [Test]
    public async Task AGhostWatchingAnInterfaceItCantUseSeesAnOrangeEye()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        var (_, user) = await OpenLathe();

        await Server.WaitPost(() =>
        {
            SEntMan.System<MobStateSystem>().ChangeMobState(user, MobState.Dead);
            SEntMan.AddComponent<GhostComponent>(user);
        });
        await Pair.RunTicksSync(90);

        await Client.WaitAssertion(() =>
        {
            AssertShows(LatheWindow(), KsTguiIcons.Eye, KsTguiPalettes.Average, "A ghost may look but not use: an orange eye.");
            Assert.That(LatheWindow().Children.OfType<KsTguiWindowDimmer>().Single().Visible, Is.False, "A ghost is never washed over.");
        });
    }

    [Test]
    public async Task TheIconIsA45pxCellAtTheLeftWithTheTitleAfterIt()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        DefaultWindow window = null!;
        await Client.WaitPost(() =>
        {
            window = new DefaultWindow { Title = "Plain" };
            window.OpenCentered();
        });
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var icon = Icon(window);
            var title = FindTitle(window)!;

            Assert.That(icon.Parent, Is.SameAs(title.Parent));
            Assert.That(icon.GetPositionInParent(), Is.EqualTo(0), "The icon comes before the title.");
            Assert.That(icon.Size.X, Is.EqualTo(45f), "tgui's cell is 45px wide.");
            Assert.That(title.GlobalPosition.X, Is.EqualTo(icon.GlobalPosition.X + 45f).Within(0.01f), "The title starts where the cell ends.");
            Assert.That(icon.MouseFilter, Is.EqualTo(Control.MouseFilterMode.Ignore));
            window.Close();
        });
    }

    [Test]
    public async Task TurningTheThemeOffRemovesTheIconAndRestoresTheHeader()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, false);
        await Pair.RunTicksSync(2);

        var configuration = Client.ResolveDependency<IConfigurationManager>();
        DefaultWindow window = null!;
        var titleMargin = default(Thickness);
        var rowChildren = 0;
        await Client.WaitPost(() =>
        {
            window = new DefaultWindow { Title = "Plain" };
            window.OpenCentered();
            titleMargin = FindTitle(window)!.Margin;
            rowChildren = FindTitle(window)!.Parent!.ChildCount;
            configuration.SetCVar(KsCCVars.TguiStyle, true);
        });
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            Assert.That(KsTguiChromeUIController.GetStatusIcon(window), Is.Not.Null);
            configuration.SetCVar(KsCCVars.TguiStyle, false);
        });
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            Assert.That(KsTguiChromeUIController.GetStatusIcon(window), Is.Null);
            Assert.That(FindTitle(window)!.Margin, Is.EqualTo(titleMargin), "The title's margin is as it was.");
            Assert.That(FindTitle(window)!.Parent!.ChildCount, Is.EqualTo(rowChildren), "The title row holds what it held.");
            window.Close();
        });
    }

    [Test]
    public async Task DraggingFromTheIconMovesTheWindow()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        DefaultWindow window = null!;
        await Client.WaitPost(() =>
        {
            window = new DefaultWindow { Title = "Plain" };
            window.OpenCentered();
        });
        await Pair.RunTicksSync(3);
        await AssertDragMoves(window);
        await Client.WaitPost(() => window.Close());

        await OpenLathe();
        await Pair.RunTicksSync(3);
        await AssertDragMoves(LatheWindow());
    }

    private async Task AssertDragMoves(BaseWindow window)
    {
        var start = Vector2.Zero;
        await Client.WaitPost(() =>
        {
            start = window.Position;
            DragFromIcon(window);
        });
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() => Assert.That(window.Position.X, Is.GreaterThan(start.X + 5f), "Dragging from the icon moves the window. "));
    }

    private void DragFromIcon(BaseWindow window)
    {
        var uiManager = Client.ResolveDependency<IUserInterfaceManager>();
        var icon = Icon(window);

        var spot = new ScreenCoordinates(icon.GlobalPixelPosition + icon.PixelSize / 2f, window.Window!.Id);
        var hit = uiManager.MouseGetControl(spot);
        Assert.That(hit, Is.Not.Null.And.Not.SameAs(icon), "The icon must not take the press.");
        for (var ancestor = hit; ancestor != null; ancestor = ancestor.Parent)
            Assert.That(ancestor, Is.Not.SameAs(icon), "Nor anything inside it.");

        var type = uiManager.GetType();
        type.GetMethod("KeyBindDown")!.Invoke(uiManager, [new BoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, spot, true)]);
        var moved = new ScreenCoordinates(spot.Position + new Vector2(10f, 0f), spot.Window);
        type.GetMethod("MouseMove")!.Invoke(uiManager, [new MouseMoveEventArgs(new Vector2(10f, 0f), moved)]);
        type.GetMethod("KeyBindUp")!.Invoke(uiManager, [new BoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, moved, true)]);
    }

    private static Label? FindTitle(Control root)
    {
        foreach (var child in root.Children)
        {
            if (child is Label label && label.HasStyleClass(DefaultWindow.StyleClassWindowTitle))
                return label;

            if (FindTitle(child) is { } found)
                return found;
        }

        return null;
    }
}
