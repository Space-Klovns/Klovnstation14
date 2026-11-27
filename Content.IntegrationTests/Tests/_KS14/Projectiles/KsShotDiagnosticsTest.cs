#nullable enable
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client._KS14.Projectiles.Diagnostics;
using Content.IntegrationTests.Tests.Interaction;
using Robust.Client.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._KS14.Projectiles;

/// <summary>
///     <c>ks_shotdiag</c> prints what the client and the server each have about an entity, and marks what they differ on.
/// </summary>
[TestOf(typeof(Content.Server._KS14.Projectiles.Diagnostics.KsShotDiagnosticsCommand))]
public sealed class KsShotDiagnosticsTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    [Test]
    public async Task MarksWhatTheTwoSidesDifferOn()
    {
        var targetUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            var position = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);
            targetUid = SEntMan.SpawnEntity(PlayerPrototype, new MapCoordinates(position + new Vector2(2f, 0f), MapData.MapId));
        });
        await RunTicks(5);

        var consoleHost = Client.ResolveDependency<IClientConsoleHost>();
        var output = new StringBuilder();
        void OnAddString(object? sender, AddStringArgs args) => output.AppendLine(args.Text.ToString());

        var targetNetEntity = SEntMan.GetNetEntity(targetUid);
        await Client.WaitPost(() =>
        {
            consoleHost.AddString += OnAddString;
            consoleHost.ExecuteCommand($"ks_shotdiag {targetNetEntity.Id}");
        });
        await RunTicks(10);

        var agreeing = output.ToString();
        output.Clear();

        // Collision off on the client alone, as a corpse's is when the client has it asleep and the server does not, and not as a prediction, so no state puts it back before the client is told to look.
        await Client.WaitPost(() =>
        {
            var clientTargetUid = CEntMan.GetEntity(targetNetEntity);
            CEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().SetCanCollide(clientTargetUid, false, dirty: false);
            consoleHost.ExecuteCommand($"ks_shotdiag {targetNetEntity.Id}");
        });
        await RunTicks(10);
        await Client.WaitPost(() => consoleHost.AddString -= OnAddString);

        var differing = output.ToString();
        string Row(string text, string key) => text.Split('\n').FirstOrDefault(line => line.Contains(key)) ?? string.Empty;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Row(agreeing, "can collide"), Does.Contain("True").And.Not.StartWith("!"),
                $"the two sides agreeing was not printed as such:\n{agreeing}");
            Assert.That(Row(agreeing, "your ping"), Is.Not.Empty, $"the server's half is missing:\n{agreeing}");
            Assert.That(Row(differing, "can collide"), Does.StartWith("!").And.Contain("False").And.Contain("True"),
                $"the two sides differing was not marked:\n{differing}");
        }
    }
}
