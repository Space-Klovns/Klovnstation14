#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Server._KS14.FieldCommander.Admin;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Shared._KS14.FieldCommander;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     What an admin changes in a field commander, through its window or its console commands, is written to the
///         admin log with who did it.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderAdminEui))]
[TestOf(typeof(KsFieldCommanderCommandLogSystem))]
public sealed class KsFieldCommanderAdminLogTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true, AdminLogsEnabled = true, DummyTicker = false };

    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _rule = "KsTestFcAdminLogRule";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFcAdminLogUnit
  components:
  - type: MobState

- type: entity
  parent: BaseGameRule
  id: KsTestFcAdminLogRule
  components:
  - type: KsFieldCommanderRule
    uplinkPrototype: null
    briefingAnnouncement: null
    phases:
    - name: ks-fieldcommander-phase-probing
      start: 1000
      waveInterval: 1000
      budget: 5
    assets:
    - entity: KsTestFcAdminLogUnit
";

    [Test]
    public async Task WindowChangesAndCommandsAreLoggedWithTheAdmin()
    {
        var testMap = await Pair.CreateTestMap();
        var session = Pair.Player!;
        var announcement = $"Admin log test {Guid.NewGuid()}";
        // A seconds argument nobody else would type, to find this run's line by.
        var stuckMarker = $"{Random.Shared.Next(100000, 999999)}.25";
        EntityUid ruleUid = default;

        await Server.WaitPost(() =>
        {
            Server.EntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            ruleUid = KsFieldCommanderTestHelpers.StartRules(Server, _rule).CommanderUid;

            var adminManager = Server.ResolveDependency<IAdminManager>();
            adminManager.PromoteHost(session);
            if (!adminManager.IsAdmin(session))
                adminManager.ReAdmin(session);
        });

        await Pair.RunTicksSync(3);

        await Server.WaitPost(() =>
        {
            var ruleNetEntity = Server.EntMan.GetNetEntity(ruleUid);
            var eui = Server.System<KsFieldCommanderAdminEuiSystem>().OpenEui(session);
            eui.HandleMessage(new KsFieldCommanderAdminQueueWaveMessage(ruleNetEntity, [new KsFieldCommanderAdminPurchase(0, 1)], announcement, false));

            Server.ResolveDependency<IConsoleHost>().ExecuteCommand(session, $"ks_fcstuck {stuckMarker}");
        });

        var adminLogManager = Server.ResolveDependency<IAdminLogManager>();
        await PoolManager.WaitUntil(Server, async () =>
        {
            var windowLogs = await adminLogManager.CurrentRoundLogs(new LogFilter { Search = announcement });
            var commandLogs = await adminLogManager.CurrentRoundLogs(new LogFilter { Search = stuckMarker });
            return windowLogs.Count > 0 && commandLogs.Count > 0;
        });

        var windowLog = (await adminLogManager.CurrentRoundLogs(new LogFilter { Search = announcement })).Single();
        var commandLog = (await adminLogManager.CurrentRoundLogs(new LogFilter { Search = stuckMarker })).Single();
        Assert.Multiple(() =>
        {
            Assert.That(windowLog.Type, Is.EqualTo(LogType.AdminCommands));
            Assert.That(windowLog.Message, Does.Contain("queued a wave"), windowLog.Message);
            Assert.That(windowLog.Players, Does.Contain(session.UserId.UserId), "the window's log does not name the admin");
            Assert.That(commandLog.Type, Is.EqualTo(LogType.AdminCommands));
            Assert.That(commandLog.Message, Does.Contain("ks_fcstuck"), commandLog.Message);
            Assert.That(commandLog.Players, Does.Contain(session.UserId.UserId), "the command's log does not name the admin");
        });
    }
}
