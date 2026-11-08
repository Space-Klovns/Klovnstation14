using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Client.Power;
using Content.Shared.Power;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     A thermomachine's body stays on show, and only the overlay above it comes and goes.
/// </summary>
public sealed class KsThermomachineSpriteTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    /// <summary>
    ///     The layers aren't named here, so this holds whatever a machine's body is drawn from.
    /// </summary>
    [TestCase("GasThermoMachineFreezer")]
    [TestCase("GasThermoMachineHeater")]
    [TestCase("GasThermoMachineHellfireFreezer")]
    [TestCase("GasThermoMachineHellfireHeater")]
    public async Task OnlyTheOverlayFollowsPower(string prototype)
    {
        var testMap = await Pair.CreateTestMap();

        EntityUid serverUid = default;
        await Server.WaitPost(() => serverUid = Server.EntMan.SpawnEntity(prototype, testMap.GridCoords));
        await Pair.RunTicksSync(5);

        var clientUid = Client.EntMan.GetEntity(Server.EntMan.GetNetEntity(serverUid));
        var spriteSystem = Client.System<SpriteSystem>();

        async Task<(bool Body, bool Overlay)> VisibleWhen(bool powered)
        {
            await Server.WaitPost(() => Server.System<SharedAppearanceSystem>().SetData(serverUid, PowerDeviceVisuals.Powered, powered));
            await Pair.RunTicksSync(5);

            var result = (false, false);
            await Client.WaitAssertion(() =>
            {
                var entity = (clientUid, Client.EntMan.GetComponent<SpriteComponent>(clientUid));
                Assert.That(spriteSystem.LayerMapTryGet(entity, PowerDeviceVisualLayers.Powered, out var overlayLayer, false));
                Assert.That(spriteSystem.TryGetLayer(entity, 0, out var bodyLayer, false));
                Assert.That(overlayLayer, Is.Not.Zero, "The overlay is the bottom layer, so nothing is under it.");
                Assert.That(spriteSystem.TryGetLayer(entity, overlayLayer, out var overlay, false));
                result = (bodyLayer!.Visible, overlay!.Visible);
            });
            return result;
        }

        var on = await VisibleWhen(true);
        var off = await VisibleWhen(false);

        Assert.Multiple(() =>
        {
            Assert.That(on.Body, "The body vanished while running.");
            Assert.That(off.Body, "The body vanished while off.");
            Assert.That(on.Overlay, "The overlay doesn't show while running.");
            Assert.That(off.Overlay, Is.False, "The overlay shows while off.");
        });
    }
}
