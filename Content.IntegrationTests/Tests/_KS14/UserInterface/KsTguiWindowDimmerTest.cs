#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Windows.Lathe;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.Ghost;
using Content.Shared.Lathe;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     tg's window dimmer. In SS14 a user who can't interact any more keeps the window open, and the server refuses its
///         messages, so the tgui window is washed over exactly then, and never for a ghost or under the Baron theme.
/// </summary>
[TestOf(typeof(KsTguiWindowDimmer))]
public sealed class KsTguiWindowDimmerTest : GameTest
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

    private KsTguiWindowDimmer? Dimmer()
    {
        var window = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<KsTguiLatheWindow>().SingleOrDefault();
        return window?.Children.OfType<KsTguiWindowDimmer>().SingleOrDefault();
    }

    [Test]
    public async Task TheWindowIsDimmedWhileItsUserCantUseIt()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        var (lathe, user) = await OpenLathe();

        await Client.WaitAssertion(() =>
        {
            Assert.That(Dimmer(), Is.Not.Null, "The window should have a dimmer.");
            Assert.That(Dimmer()!.Visible, Is.False, "A user who can interact sees no dimmer.");
        });

        // Dead: the interface stays open, which is the state the dimmer shows.
        await Server.WaitPost(() => SEntMan.System<MobStateSystem>().ChangeMobState(user, MobState.Dead));
        await Pair.RunTicksSync(30);

        await Client.WaitAssertion(() => Assert.That(Dimmer()?.Visible, Is.True, "A user who can't interact sees the window dimmed."));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedUserInterfaceSystem>().IsUiOpen(lathe, LatheUiKey.Key, user), Is.True, "Nothing closed the interface.");
        });

        // Alive again.
        await Server.WaitPost(() => SEntMan.System<MobStateSystem>().ChangeMobState(user, MobState.Alive));
        await Pair.RunTicksSync(30);
        await Client.WaitAssertion(() => Assert.That(Dimmer()?.Visible, Is.False, "Able to interact again, the dimmer is gone."));

        // A ghost is never dimmed.
        await Server.WaitPost(() =>
        {
            SEntMan.System<MobStateSystem>().ChangeMobState(user, MobState.Dead);
            SEntMan.AddComponent<GhostComponent>(user);
        });
        await Pair.RunTicksSync(30);
        await Client.WaitAssertion(() => Assert.That(Dimmer()?.Visible, Is.False, "A ghost is never dimmed."));
    }

    [Test]
    public async Task BaronNeverDims()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, false);
        await Pair.RunTicksSync(2);

        var (_, user) = await OpenLathe();
        await Server.WaitPost(() => SEntMan.System<MobStateSystem>().ChangeMobState(user, MobState.Dead));
        await Pair.RunTicksSync(30);

        await Client.WaitAssertion(() =>
        {
            var visible = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
                .SelectMany(window => window.Children.OfType<KsTguiWindowDimmer>())
                .Any(dimmer => dimmer.Visible);
            Assert.That(visible, Is.False);
        });
    }
}
