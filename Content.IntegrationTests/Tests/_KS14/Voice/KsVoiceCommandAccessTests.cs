using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Administration.Managers;
using Robust.Client.Console;
using Robust.Server.Console;

namespace Content.IntegrationTests.Tests._KS14.Voice;

/// <summary>
///     Who may run the voice commands: the moderation commands are locked to moderators, and the player commands
///         that open the voice page are open to everyone.
/// </summary>
public sealed class KsVoiceCommandAccessTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly string[] ModeratorCommands = ["vcmute", "vcunmute", "vcmutes"];
    private static readonly string[] PlayerCommands = ["voicechat", "voicelink"];

    [Test]
    public async Task ModerationCommandsAreLockedForPlayers()
    {
        var player = await Server.AddDummySession();
        var conGroupController = Server.ResolveDependency<IConGroupController>();

        await Server.WaitAssertion(() =>
        {
            foreach (var command in ModeratorCommands)
            {
                Assert.That(conGroupController.CanCommand(player, command), Is.False,
                    $"a player without admin rights must not be able to run {command}");
                Assert.That(conGroupController.CanCommand(ServerSession!, command), Is.True,
                    $"an admin must be able to run {command}");
            }
        });
    }

    [Test]
    public async Task PlayerCommandsAreOpenToEveryone()
    {
        var adminManager = Server.ResolveDependency<IAdminManager>();
        var clientConGroupController = Client.ResolveDependency<IClientConGroupController>();

        // The pooled player is a host admin, who can run anything; take that away for the check.
        await Server.WaitPost(() => adminManager.DeAdmin(ServerSession!));
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            Assert.That(clientConGroupController.CanCommand("vv"), Is.False,
                "control: de-adminning must actually take admin-only client commands away");

            foreach (var command in PlayerCommands)
            {
                Assert.That(clientConGroupController.CanCommand(command), Is.True,
                    $"{command} must be usable by every player");
            }
        });

        await Server.WaitPost(() => adminManager.ReAdmin(ServerSession!));
        await Pair.RunTicksSync(10);
    }
}
