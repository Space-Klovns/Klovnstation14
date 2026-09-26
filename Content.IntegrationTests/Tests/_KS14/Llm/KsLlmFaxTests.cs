using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.Llm;
using Content.Server.AlertLevel;
using Content.Server.Fax;
using Content.Server.Station.Systems;
using Content.Shared._KS14.CCVar;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Fax.Components;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Paper;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Serilog.Events;

namespace Content.IntegrationTests.Tests._KS14.Llm;

/// <summary>
///     Faxing Central Command drives a turn with the LLM persona, against <see cref="FakeKsLlmHandler"/> in place
///         of llama-server. <see cref="KsCCVars.LlmEndpoint"/> is set, so no process is ever spawned.
/// </summary>
[TestOf(typeof(KsLlmManager))]
public sealed class KsLlmFaxTests : GameTest
{
    // The manager's transport is swapped out, so this server must not be handed to anyone else.
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const string CentcommStamp = "stamp-component-stamped-name-centcom";
    private const string TestStationMapId = "KsLlmTestStationMap";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  parent: [ BaseStation, BaseStationAlertLevels ]
  id: KsLlmTestStation
  categories: [ HideSpawnMenu ]

- type: gameMap
  id: {TestStationMapId}
  minPlayers: 0
  mapName: {TestStationMapId}
  mapPath: /Maps/Test/empty.yml
  stations:
    Station:
      mapNameTemplate: {TestStationMapId}
      stationProto: KsLlmTestStation
      components: []
";

    private FakeKsLlmHandler _fakeHandler = default!;
    private KsLlmManager _llmManager = default!;
    private TestMapData _testMap = default!;
    private EntityUid _centcommFaxUid;
    private EntityUid _senderFaxUid;
    private string _senderAddress = string.Empty;

    private async Task SetUpFaxes(bool enable = true)
    {
        _fakeHandler = new FakeKsLlmHandler();
        _llmManager = Server.ResolveDependency<KsLlmManager>();

        // The pooled server outlives a test even when dirty, and the conversation lives on a manager, not an
        // entity - so a previous test's history would otherwise still be in the next one's requests.
        await Server.WaitPost(() =>
        {
            _llmManager.HandlerOverride = _fakeHandler;
            _llmManager.ResetConversation();
        });
        await OverrideCVar(Side.Server, KsCCVars.LlmEndpoint, "http://llm.test");

        _testMap = await Pair.CreateTestMap();
        await Server.WaitPost(() =>
        {
            _centcommFaxUid = SEntMan.SpawnEntity("FaxMachineCentcom", _testMap.GridCoords);
            _senderFaxUid = SEntMan.SpawnEntity("FaxMachineBase", _testMap.GridCoords);
        });

        await Pair.RunTicksSync(5);
        _senderAddress = SEntMan.GetComponent<DeviceNetworkComponent>(_senderFaxUid).Address;
        Assert.That(_senderAddress, Is.Not.Empty, "the sender fax never joined the device network");

        if (enable)
            await Enable();
    }

    private async Task Enable()
    {
        await OverrideCVar(Side.Server, KsCCVars.LlmEnabled, true);
        await WaitUntil(() => _llmManager.State == KsLlmState.Ready, "the backend never became ready");
    }

    /// <summary>
    ///     Ticks the server until <paramref name="condition"/> holds. The fake answers on the thread pool, in real
    ///         time, so this yields between ticks rather than counting on a fixed number of them.
    /// </summary>
    private async Task WaitUntil(Func<bool> condition, string failureMessage, int maxTicks = 600)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            var satisfied = false;
            await Server.WaitPost(() => satisfied = condition());
            if (satisfied)
                return;

            await Pair.RunTicksSync(1);
            await Task.Delay(2);
        }

        Assert.Fail(failureMessage);
    }

    private async Task SendFax(string content, params StampDisplayInfo[] stamps)
    {
        await Server.WaitPost(() =>
        {
            var printout = new FaxPrintout(content,
                "Request",
                stampState: stamps.Length > 0 ? "paper_stamp-centcom" : null,
                stampedBy: stamps.ToList(),
                senderFaxName: "Bridge");

            Server.System<FaxSystem>().Receive(_centcommFaxUid, printout, _senderAddress);
        });
    }

    private List<string> GetReplies()
    {
        return SEntMan.GetComponent<FaxMachineComponent>(_senderFaxUid).PrintingQueue.Select(printout => printout.Content).ToList();
    }

    private static StampDisplayInfo Stamp(string name)
    {
        return new StampDisplayInfo { StampedName = name, StampedColor = Color.Red };
    }

    [Test]
    public async Task DisabledSendsNothing()
    {
        await SetUpFaxes(enable: false);

        await SendFax("Is anyone there?");
        await Pair.RunTicksSync(30);
        Assert.That(_fakeHandler.Requests, Is.Empty, "a fax must not reach the model while the feature is disabled");

        // Control: the same fax does go through once enabled, so the assertion above can fail.
        await Enable();
        await SendFax("Is anyone there?");
        await WaitUntil(() => GetReplies().Count == 1, "no reply was faxed back once enabled");
        Assert.That(_fakeHandler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task RoundTripCarriesTextAndStamps()
    {
        await SetUpFaxes();
        _fakeHandler.EnqueueText("Request denied. Nanotrasen Central Command");

        await SendFax("[bold]Requesting[/bold] more clowns.", Stamp("stamp-component-stamped-name-captain"));
        await WaitUntil(() => GetReplies().Count == 1, "no reply was faxed back");

        var messages = FakeKsLlmHandler.GetMessages(_fakeHandler.Requests.Single());
        var userMessage = messages.Last(message => message.GetProperty("role").GetString() == "user").GetProperty("content").GetString()!;

        await Server.WaitAssertion(() =>
        {
            var printout = SEntMan.GetComponent<FaxMachineComponent>(_senderFaxUid).PrintingQueue.Single();
            Assert.Multiple(() =>
            {
                Assert.That(messages[0].GetProperty("role").GetString(), Is.EqualTo("system"));
                Assert.That(userMessage, Does.Contain("Requesting more clowns."), "the paper text, markup removed, must be in the prompt");
                Assert.That(userMessage, Does.Not.Contain("[bold]"));
                Assert.That(userMessage, Does.Contain("Captain"), "the stamps must be in the prompt");
                Assert.That(printout.Content, Is.EqualTo("Request denied. Nanotrasen Central Command"));
                Assert.That(printout.StampedBy.Select(stamp => stamp.StampedName), Does.Contain(CentcommStamp), "replies are stamped by Central Command");
            });
        });
    }

    [Test]
    public async Task DoesNotBlockTheTick()
    {
        await SetUpFaxes();
        var gate = new TaskCompletionSource();
        _fakeHandler.EnqueueGated(gate.Task, "Finally.");

        await SendFax("Hello?");
        await WaitUntil(() => _fakeHandler.Requests.Count == 1, "the request was never sent");

        var startTick = SGameTiming.CurTick;
        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SGameTiming.CurTick.Value - startTick.Value, Is.EqualTo(60u), "the server must keep ticking while the model thinks");
            Assert.That(_llmManager.TurnInFlight, Is.True);
            Assert.That(GetReplies(), Is.Empty);
        });

        gate.SetResult();
        await WaitUntil(() => GetReplies().Count == 1, "the reply never arrived after the backend answered");
    }

    [Test]
    public async Task ToolResultIsFedBack()
    {
        await SetUpFaxes();
        _fakeHandler.EnqueueToolCall("get_station_status", "{}");
        _fakeHandler.EnqueueText("Status noted.");

        await SendFax("How are we doing?");
        await WaitUntil(() => GetReplies().Count == 1, "no reply was faxed back");

        var requests = _fakeHandler.Requests;
        Assert.That(requests, Has.Count.EqualTo(2));

        var followUp = FakeKsLlmHandler.GetMessages(requests[1]);
        var toolMessage = followUp.Single(message => message.GetProperty("role").GetString() == "tool");
        var assistantMessage = followUp.Single(message => message.TryGetProperty("tool_calls", out _));

        Assert.Multiple(() =>
        {
            Assert.That(toolMessage.GetProperty("tool_call_id").GetString(),
                Is.EqualTo(assistantMessage.GetProperty("tool_calls")[0].GetProperty("id").GetString()));
            // The test map is no station; the effect ran and said so.
            Assert.That(toolMessage.GetProperty("content").GetString(), Does.Contain("does not belong to any station"));
        });
    }

    [Test]
    public async Task EnumMeaningsReachTheModel()
    {
        await SetUpFaxes();
        await SendFax("What do the alert levels mean?");
        await WaitUntil(() => GetReplies().Count == 1, "no reply was faxed back");

        using var request = JsonDocument.Parse(_fakeHandler.Requests.Single());
        var alertTool = request.RootElement.GetProperty("tools").EnumerateArray()
            .Single(tool => tool.GetProperty("function").GetProperty("name").GetString() == "set_alert_level");
        var levelDescription = alertTool.GetProperty("function").GetProperty("parameters")
            .GetProperty("properties").GetProperty("level").GetProperty("description").GetString();

        Assert.That(levelDescription, Does.Contain("'yellow': A structural or atmospheric hazard"),
            "each level's meaning from YAML must be in the schema the model sees");
    }

    [Test]
    public async Task ToolLoopIsBounded()
    {
        await SetUpFaxes();
        await OverrideCVar(Side.Server, KsCCVars.LlmMaxToolTurns, 2);
        _fakeHandler.EnqueueToolCall("get_station_status", "{}");
        _fakeHandler.EnqueueToolCall("get_station_status", "{}");
        _fakeHandler.EnqueueText("Enough.");

        await SendFax("Loop forever, please.");
        await WaitUntil(() => GetReplies().Count == 1, "no reply was faxed back");

        var requests = _fakeHandler.Requests;
        Assert.That(requests, Has.Count.EqualTo(3));

        using var first = JsonDocument.Parse(requests[0]);
        using var last = JsonDocument.Parse(requests[2]);
        Assert.Multiple(() =>
        {
            Assert.That(first.RootElement.TryGetProperty("tool_choice", out _), Is.False);
            Assert.That(last.RootElement.GetProperty("tool_choice").GetString(), Is.EqualTo("none"),
                "past the tool-turn limit, the model must be made to answer in text");
        });
    }

    [Test]
    public async Task BadArgumentsBecomeErrors()
    {
        await SetUpFaxes();
        _fakeHandler.EnqueueToolCall("set_alert_level", "{not json");
        _fakeHandler.EnqueueToolCall("set_alert_level", "{\"level\": 5}");
        _fakeHandler.EnqueueToolCall("set_alert_level", "{\"level\": \"delta\"}");
        _fakeHandler.EnqueueToolCall("no_such_tool", "{}");
        _fakeHandler.EnqueueText("Never mind.");

        await SendFax("Set delta.");
        await WaitUntil(() => GetReplies().Count == 1, "no reply was faxed back");

        var toolResults = FakeKsLlmHandler.GetMessages(_fakeHandler.Requests.Last())
            .Where(message => message.GetProperty("role").GetString() == "tool")
            .Select(message => message.GetProperty("content").GetString()!)
            .ToList();

        Assert.That(toolResults, Has.Count.EqualTo(4));
        Assert.Multiple(() =>
        {
            Assert.That(toolResults[0], Does.Contain("not valid JSON"));
            Assert.That(toolResults[1], Does.Contain("must be a string"));
            Assert.That(toolResults[2], Does.Contain("must be one of"));
            Assert.That(toolResults[3], Does.Contain("no tool named"));
        });
    }

    [Test]
    public async Task CruiseMissileNeedsCentcommStampAndFiresOnce()
    {
        await SetUpFaxes();

        // Make the grid a station, and put the player's body on it under a known name.
        EntityUid targetUid = default;
        await Server.WaitPost(() =>
        {
            var stationConfig = SProtoMan.Index<GameMapPrototype>(TestStationMapId).Stations["Station"];
            Server.System<StationSystem>().InitializeNewStation(stationConfig, [_testMap.Grid.Owner], "Test Station");

            targetUid = SEntMan.SpawnEntity("MobHuman", _testMap.GridCoords);
            SEntMan.System<MetaDataSystem>().SetEntityName(targetUid, "Rob Robertson");
            Server.PlayerMan.SetAttachedEntity(ServerSession!, targetUid);
        });

        const string missile = "SupplyPodMissileNtInert";
        int CountMissiles()
        {
            var count = 0;
            var metaDataQuery = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
            while (metaDataQuery.MoveNext(out var metaDataComponent))
            {
                if (metaDataComponent.EntityPrototype?.ID == missile)
                    count++;
            }

            return count;
        }
        const string strike = "{\"target_name\": \"rob robertson\", \"armed\": false}";

        // Stamped, but not by anyone who may authorise a strike: the model may ask, the effect must refuse.
        _fakeHandler.EnqueueToolCall("launch_cruise_missile", strike);
        _fakeHandler.EnqueueText("Denied.");
        await SendFax("Strike Rob Robertson.", Stamp("stamp-component-stamped-name-clown"));
        await WaitUntil(() => GetReplies().Count == 1, "no reply to the unstamped request");
        await Server.WaitAssertion(() => Assert.That(CountMissiles(), Is.Zero, "a strike without an authorising stamp must be refused"));

        var refusal = FakeKsLlmHandler.GetMessages(_fakeHandler.Requests.Last())
            .Last(message => message.GetProperty("role").GetString() == "tool")
            .GetProperty("content").GetString();
        Assert.That(refusal, Does.Contain("Head of Security"), "the refusal must name the stamps the YAML accepts");

        // Stamped: it fires.
        _fakeHandler.EnqueueToolCall("launch_cruise_missile", strike);
        _fakeHandler.EnqueueText("Done.");
        await SendFax("Strike Rob Robertson.", Stamp(CentcommStamp));
        await WaitUntil(() => GetReplies().Count == 2, "no reply to the stamped request");
        await Server.WaitAssertion(() => Assert.That(CountMissiles(), Is.EqualTo(1), "a stamped strike on a station crew member must fire"));

        // Stamped again: the round's one missile is spent.
        _fakeHandler.EnqueueToolCall("launch_cruise_missile", strike);
        _fakeHandler.EnqueueText("Out of missiles.");
        await SendFax("Strike Rob Robertson again.", Stamp(CentcommStamp));
        await WaitUntil(() => GetReplies().Count == 3, "no reply to the repeat request");

        var lastToolResult = FakeKsLlmHandler.GetMessages(_fakeHandler.Requests.Last())
            .Last(message => message.GetProperty("role").GetString() == "tool")
            .GetProperty("content").GetString();

        await Server.WaitAssertion(() =>
        {
            Assert.That(CountMissiles(), Is.EqualTo(1), "only one missile per round");
            Assert.That(lastToolResult, Does.Contain("per shift"));
        });
    }

    [Test]
    public async Task AlertLevelIsChanged()
    {
        await SetUpFaxes();

        EntityUid stationUid = default;
        await Server.WaitPost(() =>
        {
            var stationConfig = SProtoMan.Index<GameMapPrototype>(TestStationMapId).Stations["Station"];
            stationUid = Server.System<StationSystem>().InitializeNewStation(stationConfig, [_testMap.Grid.Owner], "Test Station");
        });

        _fakeHandler.EnqueueToolCall("set_alert_level", "{\"level\": \"blue\"}");
        _fakeHandler.EnqueueText("Blue it is.");
        await SendFax("Raise the alert, we have suspicious activity.");
        await WaitUntil(() => GetReplies().Count == 1, "no reply was faxed back");

        await Server.WaitAssertion(() =>
            Assert.That(Server.System<AlertLevelSystem>().GetLevel(stationUid), Is.EqualTo("blue")));
    }

    [Test]
    public async Task CompactsNearTheContextLimit()
    {
        await SetUpFaxes();
        await OverrideCVar(Side.Server, KsCCVars.LlmCompactKeepMessages, 1);

        // First exchange reports a context nearly full.
        _fakeHandler.EnqueueText("First reply.", promptTokens: FakeKsLlmHandler.ContextSize - 100);
        await SendFax("First fax.");
        await WaitUntil(() => GetReplies().Count == 1, "no reply to the first fax");

        // So the second fax is preceded by a compaction request, whose answer lands in the system prompt.
        _fakeHandler.EnqueueText("SUMMARY-OF-FIRST-FAX");
        _fakeHandler.EnqueueText("Second reply.");
        await SendFax("Second fax.");
        await WaitUntil(() => GetReplies().Count == 2, "no reply to the second fax");

        var requests = _fakeHandler.Requests;
        Assert.That(requests, Has.Count.EqualTo(3));

        var compactionMessages = FakeKsLlmHandler.GetMessages(requests[1]);
        var afterMessages = FakeKsLlmHandler.GetMessages(requests[2]);
        Assert.Multiple(() =>
        {
            Assert.That(compactionMessages[1].GetProperty("content").GetString(), Does.Contain("First fax."), "the old exchange is what gets summarised");
            Assert.That(afterMessages[0].GetProperty("content").GetString(), Does.Contain("SUMMARY-OF-FIRST-FAX"));
            Assert.That(afterMessages.Any(message => message.GetProperty("content").GetString()!.Contains("First reply.")), Is.False,
                "the summarised messages must be gone from the history");
            Assert.That(afterMessages.Last().GetProperty("content").GetString(), Does.Contain("Second fax."));
        });
    }

    [Test]
    public async Task ApiKeyIsSent()
    {
        await SetUpFaxes(enable: false);
        _fakeHandler.RequiredApiKey = "hunter2";
        await OverrideCVar(Side.Server, KsCCVars.LlmApiKey, "hunter2");
        await Enable();

        await SendFax("Authenticated fax.");
        await WaitUntil(() => GetReplies().Count == 1, "no reply was faxed back");

        Assert.That(_fakeHandler.LastAuthorization, Is.EqualTo("Bearer hunter2"));
    }

    [Test]
    public async Task WrongApiKeyFailsAtConnect()
    {
        await SetUpFaxes(enable: false);
        _fakeHandler.RequiredApiKey = "right";
        await OverrideCVar(Side.Server, KsCCVars.LlmApiKey, "wrong");

        // The rejection, and the refusal to retry it, are logged as errors on purpose; that is the loud
        // failure being tested for.
        var rejections = 0;
        bool Judge(string sawmill, LogEvent message)
        {
            if (sawmill != "llm")
                return false;

            var rendered = message.RenderMessage();
            if (rendered.Contains("rejected the API key"))
            {
                rejections++;
                return true;
            }

            return rendered.Contains("not retrying");
        }

        Pair.ServerLogHandler.JudgeLog += Judge;
        try
        {
            await OverrideCVar(Side.Server, KsCCVars.LlmEnabled, true);
            await WaitUntil(() => _llmManager.State == KsLlmState.Failed, "a rejected key must fail, not retry forever");
        }
        finally
        {
            Pair.ServerLogHandler.JudgeLog -= Judge;
        }

        Assert.That(rejections, Is.EqualTo(1));
    }

    [Test]
    public async Task UnreachableRemoteIsRetriedUntilItAnswers()
    {
        await SetUpFaxes(enable: false);
        await OverrideCVar(Side.Server, KsCCVars.LlmStartupTimeout, 0.5f);
        // A local process would be given up on after zero restarts; a remote must not be.
        await OverrideCVar(Side.Server, KsCCVars.LlmRestartAttempts, 0);
        _fakeHandler.Unreachable = true;

        await OverrideCVar(Side.Server, KsCCVars.LlmEnabled, true);
        await WaitUntil(() => _llmManager.State == KsLlmState.Restarting, "an unreachable remote must be scheduled for another try");

        // Past the first retry, where a local backend with zero restart attempts would already have failed.
        var attemptsBefore = _fakeHandler.Attempts;
        await WaitUntil(() => _fakeHandler.Attempts > attemptsBefore, "the remote was never tried again", maxTicks: 5000);
        await WaitUntil(() => _llmManager.State == KsLlmState.Restarting, "it must go back to waiting, not give up", maxTicks: 5000);

        _fakeHandler.Unreachable = false;
        await WaitUntil(() => _llmManager.State == KsLlmState.Ready, "the remote was not picked back up once it answered", maxTicks: 5000);

        await SendFax("Are you back?");
        await WaitUntil(() => GetReplies().Count == 1, "no reply after reconnecting");
    }

    [Test]
    public async Task LosingTheRemoteMidRoundReconnects()
    {
        await SetUpFaxes();
        await OverrideCVar(Side.Server, KsCCVars.LlmStartupTimeout, 0.5f);

        _fakeHandler.Unreachable = true;
        await SendFax("Anyone there?");
        await WaitUntil(() => _llmManager.State == KsLlmState.Restarting, "a lost remote must be noticed and retried");
        Assert.That(GetReplies(), Is.Empty, "a fax sent while the remote is gone goes unanswered");

        _fakeHandler.Unreachable = false;
        await WaitUntil(() => _llmManager.State == KsLlmState.Ready, "the remote was not picked back up", maxTicks: 5000);

        await SendFax("Now?");
        await WaitUntil(() => GetReplies().Count == 1, "no reply after reconnecting");
    }

    [Test]
    public async Task RoundRestartForgetsTheConversation()
    {
        await SetUpFaxes();
        await SendFax("Remember me.");
        await WaitUntil(() => GetReplies().Count == 1, "no reply was faxed back");

        await Server.WaitAssertion(() =>
        {
            Assert.That(_llmManager.GetConversation("KsLlmCentralCommand"), Is.Not.Null);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent());
            Assert.That(_llmManager.GetConversation("KsLlmCentralCommand"), Is.Null);
        });
    }
}
