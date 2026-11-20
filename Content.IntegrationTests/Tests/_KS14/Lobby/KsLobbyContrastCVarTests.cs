using System.Threading.Tasks;
using Content.Client.Lobby.UI;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.Lobby;
using Content.Shared._KS14.CCVar;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._KS14.Lobby;

/// <summary>
///     The adaptive contrast cvar puts the backdrop shader on the lobby background and takes it off again, and a
///         disposed lobby stops listening to the cvar. Nothing headless draws, so the shader itself is unchecked.
/// </summary>
[TestOf(typeof(LobbyGui))]
public sealed class KsLobbyContrastCVarTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task CVarTogglesBackgroundShader()
    {
        // Start in the tactical layout whatever the date: the default, auto, is Kharkiv on clown holidays.
        await OverrideCVar(Side.Server, KsCCVars.LobbyLayout, KsLobbyLayouts.Tactical);
        await Pair.RunTicksSync(5);

        var configurationManager = Client.Resolve<IConfigurationManager>();
        LobbyGui lobby = default!;

        await Client.WaitPost(() =>
        {
            configurationManager.SetCVar(KsCCVars.LobbyAdaptiveContrast, true);
            lobby = new LobbyGui();
            Client.Resolve<IUserInterfaceManager>().WindowRoot.AddChild(lobby);
        });

        try
        {
            await Client.WaitAssertion(() =>
            {
                Assert.That(lobby.Background.ShaderOverride, Is.Not.Null, "cvar on");
                Assert.That(lobby.Background.ShaderOverride, Is.Not.SameAs(lobby.KsKharkiv.BackdropShader),
                    "tactical: the dimming, not the grade");
                Assert.That(lobby.KsViewport.KsDrawShader, Is.Not.Null, "cvar on, viewport");
            });

            // The Kharkiv layout puts its grade on the art instead, and the cvar takes it off the same way.
            await Client.WaitPost(() => lobby.KsSetLayout(KsLobbyLayout.Kharkiv));
            await Client.WaitAssertion(() => Assert.That(lobby.Background.ShaderOverride,
                Is.SameAs(lobby.KsKharkiv.BackdropShader), "Kharkiv: the grade"));
            await Client.WaitPost(() => configurationManager.SetCVar(KsCCVars.LobbyAdaptiveContrast, false));
            await Client.WaitAssertion(() => Assert.That(lobby.Background.ShaderOverride, Is.Null, "Kharkiv, cvar off"));
            await Client.WaitPost(() =>
            {
                configurationManager.SetCVar(KsCCVars.LobbyAdaptiveContrast, true);
                lobby.KsSetLayout(KsLobbyLayout.Tactical);
            });

            await Client.WaitPost(() => configurationManager.SetCVar(KsCCVars.LobbyAdaptiveContrast, false));
            await Client.WaitAssertion(() =>
            {
                Assert.That(lobby.Background.ShaderOverride, Is.Null, "cvar off");
                Assert.That(lobby.KsViewport.KsDrawShader, Is.Null, "cvar off, viewport");
            });

            // Character setup doesn't blur the backdrop: with the cvar off it has no shader either.
            await Client.WaitPost(() => lobby.SwitchState(LobbyGui.LobbyGuiState.CharacterSetup));
            await Client.WaitAssertion(() => Assert.That(lobby.Background.ShaderOverride, Is.Null, "no blur in character setup"));

            // Out of the tree the lobby no longer follows the cvar: switching it back on changes nothing here.
            await Client.WaitPost(() =>
            {
                lobby.Orphan();
                lobby.SwitchState(LobbyGui.LobbyGuiState.Default);
                configurationManager.SetCVar(KsCCVars.LobbyAdaptiveContrast, true);
            });
            await Client.WaitAssertion(() => Assert.That(lobby.Background.ShaderOverride, Is.Null, "unsubscribed"));
        }
        finally
        {
            await Client.WaitPost(() =>
            {
                lobby.Dispose();
            });
        }
    }
}
