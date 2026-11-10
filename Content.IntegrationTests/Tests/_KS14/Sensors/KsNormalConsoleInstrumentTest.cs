#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Shuttles.UI;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.Sensors;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.Wires;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Sensors;

/// <summary>
///     The normal shuttle console opens the instrument window and goes on seeing every grid: maps place it and no sensors, so a
///         sensor picture on it would be an empty scope. The KS consoles keep their fog of war.
/// </summary>
[TestOf(typeof(KsInstrumentWindow))]
public sealed class KsNormalConsoleInstrumentTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId NormalConsole = "ComputerShuttle";
    private static readonly EntProtoId SensorConsole = "KsComputerShuttle";
    private static readonly EntProtoId Human = "MobHuman";

    private EntityUid _consoleUid;

    private async Task<KsInstrumentWindow> Open(EntProtoId console)
    {
        var testMap = await Pair.CreateTestMap();
        await Server.WaitPost(() =>
        {
            _consoleUid = SEntMan.SpawnEntity(console, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            var userUid = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 1.5f)));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, userUid);
            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(_consoleUid, ShuttleConsoleUiKey.Key, userUid);
        });
        await Pair.RunTicksSync(15);

        KsInstrumentWindow window = null!;
        await Client.WaitPost(() => window = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<KsInstrumentWindow>().Single());
        return window;
    }

    [Test]
    public async Task TheNormalConsoleOpensTheInstrumentWindowWithAnOpenPicture()
    {
        var window = await Open(NormalConsole);

        await Client.WaitAssertion(() =>
        {
            Assert.That(window.KsOpenPicture, Is.True);
            Assert.That(window.KsEsmTabShown, Is.False, "Without sensors the ESM tab has nothing on it.");
            Assert.That(window.KsMapOpenPicture, Is.True, "The chart has to list the grids too.");
            Assert.That(window.KsRadarOpenPicture, Is.True, "The scope has to draw them.");
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(
                SProtoMan.Index(NormalConsole).TryGetComponent<KsSensorConsoleComponent>(out _, SEntMan.ComponentFactory),
                Is.False,
                "No map places sensors, so a sensor console here is an empty scope on every shuttle.");

            // The partial replaces the mapping's shuttle key, and upstream's other interface has to have survived it.
            Assert.That(SEntMan.System<SharedUserInterfaceSystem>().HasUi(_consoleUid, WiresUiKey.Key), Is.True);
        });
    }

    [Test]
    public async Task ASensorConsoleKeepsItsFogOfWar()
    {
        var window = await Open(SensorConsole);

        await Client.WaitAssertion(() =>
        {
            Assert.That(window.KsOpenPicture, Is.False);
            Assert.That(window.KsEsmTabShown, Is.True);
            Assert.That(window.KsMapOpenPicture, Is.False);
            Assert.That(window.KsRadarOpenPicture, Is.False);
        });
    }

    [Test]
    public void AnOpenPictureDrawsWhatUpstreamWouldAndAFogOfWarWhatItTracks()
    {
        Assert.That(KsRadarControl.KsIsForeignGridShown(openPicture: true, upstreamWouldDraw: true, liveContact: false), Is.True);
        Assert.That(KsRadarControl.KsIsForeignGridShown(openPicture: true, upstreamWouldDraw: false, liveContact: true), Is.False, "IFF hiding still counts.");
        Assert.That(KsRadarControl.KsIsForeignGridShown(openPicture: false, upstreamWouldDraw: true, liveContact: false), Is.False);
        Assert.That(KsRadarControl.KsIsForeignGridShown(openPicture: false, upstreamWouldDraw: false, liveContact: true), Is.True);
    }
}
