using System.Linq;
using Content.Client.Administration.UI;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.Administration.UI.Logs;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Administration.Commands;
using Content.Server.Administration.Logs;
using Content.Shared.Database;

namespace Content.IntegrationTests.Tests.Administration.Logs;

public sealed class LogWindowTest : InteractionTest
{
    protected override PoolSettings Settings => new() { Connected = true, Dirty = true, AdminLogsEnabled = true, DummyTicker = false };

    [Test]
    public async Task TestAdminLogsWindow()
    {
        // First, generate a new log
        var log = Server.Resolve<IAdminLogManager>();
        var guid = Guid.NewGuid();
        await Server.WaitPost(() => log.Add(LogType.Unknown, $"{SPlayer} test log 1: {guid}"));

        // Click the admin button in the menu bar
        await ClickWidgetControl<GameTopMenuBar, MenuButton>(nameof(GameTopMenuBar.AdminButton));
        var adminWindow = GetWindow<AdminMenuWindow>();

        // Find and click the "open logs" button.
        Assert.That(TryGetControlFromChildren<CommandButton>(x => x.Command == OpenAdminLogsCommand.Cmd, adminWindow, out var btn));
        await ClickControl(btn!);
        var logWindow = GetWindow<AdminLogsWindow>();

        // Find the log search field and refresh buttons
        var search = logWindow.Logs.LogSearch;
        var refresh = logWindow.Logs.RefreshButton;
        var cont = logWindow.Logs.LogsContainer;

        // Search for the log we added earlier.
        await Client.WaitPost(() => search.Text = guid.ToString());
        await ClickControl(refresh);
        await RunTicks(5);
        var searchResult = await KsAwaitSearchResult(cont, guid); /* KS14: was cont.Children.Where(x => x.Visible && x is AdminLogLabel).Cast<AdminLogLabel>().ToArray(), with only the ticks above to wait for it */
        Assert.That(searchResult.Length, Is.EqualTo(1));
        Assert.That(searchResult[0].Log.Message, Contains.Substring($" test log 1: {guid}"));

        // Add a new log
        guid = Guid.NewGuid();
        await Server.WaitPost(() => log.Add(LogType.Unknown, $"{SPlayer} test log 2: {guid}"));

        // Update the search and refresh
        await Client.WaitPost(() => search.Text = guid.ToString());
        await ClickControl(refresh);
        await RunTicks(5);
        searchResult = await KsAwaitSearchResult(cont, guid); /* KS14: was cont.Children.Where(x => x.Visible && x is AdminLogLabel).Cast<AdminLogLabel>().ToArray(), with only the ticks above to wait for it */
        Assert.That(searchResult.Length, Is.EqualTo(1));
        Assert.That(searchResult[0].Log.Message, Contains.Substring($" test log 2: {guid}"));
    }

    // KS14 start: the logs come back off a database task and the thread pool, in real time, however many ticks are run
    //   meanwhile. So wait for the search's result to show, for up to ten seconds, and then a while longer for anything
    //   else on its way: a reply to an earlier request arriving after it must not change what is shown. The asserting is
    //   left to the caller.
    private async Task<AdminLogLabel[]> KsAwaitSearchResult(Robust.Client.UserInterface.Control container, Guid guid)
    {
        var search = guid.ToString();
        AdminLogLabel[] Shown() => container.Children.Where(x => x.Visible && x is AdminLogLabel).Cast<AdminLogLabel>().ToArray();
        bool Found() => Shown() is { Length: 1 } shown && shown[0].Log.Message.Contains(search);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!Found() && stopwatch.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(10);
            await RunTicks(1);
        }

        stopwatch.Restart();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(0.5))
        {
            await Task.Delay(10);
            await RunTicks(1);
        }

        return Shown();
    }
    // KS14 end
}
