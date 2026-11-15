#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Windows.Atmos;
using Content.Client.Atmos.UI;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.Atmos.Piping.Unary.Components;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The thermomachine's bound user interface updates on every gas status state, many times a second, and must not
///         overwrite the temperature the player is typing each time (issue #478).
/// </summary>
[TestOf(typeof(GasThermomachineBoundUserInterface))]
public sealed class KsThermomachineTypingTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId Thermomachine = "GasThermoMachineFreezer";
    private static readonly EntProtoId Human = "MobHuman";

    [TestCase(true)]
    [TestCase(false)]
    public async Task AnUpdateKeepsWhatThePlayerIsTyping(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        var testMap = await Pair.CreateTestMap();
        EntityUid serverDevice = default;
        await Server.WaitPost(() =>
        {
            serverDevice = SEntMan.SpawnEntity(Thermomachine, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            var user = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 1.5f)));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, user);
            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(serverDevice, ThermomachineUiKey.Key, user);
        });
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var clientDevice = CEntMan.GetEntity(SEntMan.GetNetEntity(serverDevice));
            Assert.That(CEntMan.System<SharedUserInterfaceSystem>().TryGetOpenUi(clientDevice, ThermomachineUiKey.Key, out var boundUserInterface));

            var windows = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.ToList();
            var target = CEntMan.GetComponent<GasThermoMachineComponent>(clientDevice).TargetTemperature;
            var typed = target + 42f;

            // Half-typed: in the box but not yet sent, so the component still has the old target.
            Func<float> shown;
            if (windows.OfType<KsTguiGasThermomachineWindow>().FirstOrDefault() is { } tguiWindow)
            {
                tguiWindow.TemperatureInput.SetValue(typed);
                shown = () => tguiWindow.TemperatureInput.Value;
            }
            else
            {
                var stockWindow = windows.OfType<GasThermomachineWindow>().Single();
                stockWindow.TemperatureSpinbox.Value = typed;
                shown = () => stockWindow.TemperatureSpinbox.Value;
            }

            boundUserInterface!.Update();
            Assert.That(shown(), Is.EqualTo(typed), "a state update with an unchanged target overwrote the typed temperature");
        });
    }
}
