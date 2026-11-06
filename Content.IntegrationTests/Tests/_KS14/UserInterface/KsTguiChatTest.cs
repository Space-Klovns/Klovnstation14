#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Chat;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Chat;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.Chat;
using Robust.Client.UserInterface;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The tgui chat panel and tgui-say: pages filter what a box shows, unread counts follow the pages that aren't open, the
///         theme toggle swaps the input, and what is typed in the say bar goes out through the stock send path. How any of it
///         looks is for a real client.
/// </summary>
public sealed class KsTguiChatTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private int _messageId = 900000;

    private ChatMessage Message(ChatChannel channel, string text)
    {
        return new ChatMessage(channel, text, text, default, null) { MessageId = _messageId++ };
    }

    /// <summary>
    ///     A box the way the screens build one, attached so that it sees the controller's messages.
    /// </summary>
    private ChatBox AddBox(Control parent)
    {
        var box = new ChatBox();
        parent.AddChild(box);
        return box;
    }

    [Test]
    public async Task PagesFilterMessagesAndCountUnreadOnTheOthers()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);

        await Client.WaitPost(() =>
        {
            var ui = Client.Resolve<IUserInterfaceManager>();
            var controller = ui.GetUIController<ChatUIController>();
            var box = AddBox(ui.WindowRoot);
            box.KsSetTguiMode(true);
            var pages = controller.KsPages;
            var main = pages.Current;
            var before = box.KsEntryCount;

            controller.ProcessChatMessage(Message(ChatChannel.Local, "ks tgui local one"), speechBubble: false);
            controller.ProcessChatMessage(Message(ChatChannel.OOC, "ks tgui ooc one"), speechBubble: false);
            Assert.That(box.KsEntryCount, Is.EqualTo(before + 2), "the main page shows every channel");

            var oocPage = pages.Add("OOC only")!;
            pages.ToggleChannel(oocPage, ChatChannel.OOC);
            Assert.That(pages.Current, Is.SameAs(oocPage), "a new page opens");

            var oocInHistory = controller.History.Count(entry => oocPage.Accepts(entry.Msg.Channel));
            Assert.That(box.KsEntryCount, Is.EqualTo(oocInHistory), "the box draws only what the open page accepts");

            pages.Select(main);
            Assert.That(oocPage.Unread, Is.Zero);
            controller.ProcessChatMessage(Message(ChatChannel.Local, "ks tgui local two"), speechBubble: false);
            Assert.That(oocPage.Unread, Is.Zero, "a message the page doesn't show isn't unread on it");

            controller.ProcessChatMessage(Message(ChatChannel.OOC, "ks tgui ooc two"), speechBubble: false);
            Assert.That(oocPage.Unread, Is.EqualTo(1));
            Assert.That(box.KsHeader.TabFor(oocPage).Count, Is.EqualTo("1"), "the tab carries the count");
            Assert.That(box.KsHeader.TabFor(main).Count, Is.Null, "the open page has none");

            pages.SetHideUnread(oocPage, true);
            Assert.That(box.KsHeader.TabFor(oocPage).Count, Is.Null, "a muted page shows no count");
            pages.SetHideUnread(oocPage, false);

            pages.Select(oocPage);
            Assert.That(oocPage.Unread, Is.Zero, "opening a page reads it");

            pages.Select(main);
            pages.Remove(oocPage);
            Assert.That(pages.Pages.Contains(oocPage), Is.False);
            Assert.That(box.KsHeader.TabCount, Is.EqualTo(pages.Pages.Count));

            box.Dispose();
        });
    }

    [Test]
    public void PagesSurviveTheirCvar()
    {
        var raw = KsTguiChatPages.Serialize(
        [
            new KsTguiChatPage { Id = "main", Name = "Main", Channels = (ChatChannel)ushort.MaxValue },
            new KsTguiChatPage { Id = "page3", Name = "Radio, loud", Channels = ChatChannel.Radio, HideUnread = true },
        ]);

        var loaded = KsTguiChatPages.Deserialize(raw);
        Assert.That(loaded, Has.Count.EqualTo(2));
        Assert.That(loaded[1].Name, Is.EqualTo("Radio  loud"), "the separators are cleaned from a name");
        Assert.That(loaded[1].Channels, Is.EqualTo(ChatChannel.Radio));
        Assert.That(loaded[1].HideUnread, Is.True);
        Assert.That(KsTguiChatPages.Deserialize("garbage;;a,b"), Is.Empty);
    }

    [Test]
    public async Task TogglingTheThemeSwapsTheInput()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        var chats = Client.Resolve<IUserInterfaceManager>().GetUIController<ChatUIController>().Chats;
        await Client.WaitAssertion(() =>
        {
            Assert.That(chats, Is.Not.Empty);
            foreach (var chat in chats)
            {
                Assert.That(chat.KsTguiActive, Is.True);
                Assert.That(chat.ChatInput.Visible, Is.False, "the stock input is hidden");
                Assert.That(chat.KsHeader.Visible, Is.True);
            }
        });

        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, false);
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            foreach (var chat in chats)
            {
                Assert.That(chat.KsTguiActive, Is.False);
                Assert.That(chat.ChatInput.Visible, Is.True, "the stock input is back");
                Assert.That(chat.KsHeader.Visible, Is.False);
                Assert.That(chat.KsSay.Visible, Is.False);
            }
        });
    }

    [Test]
    public async Task TheOverlayLayoutDocksTheBarAndTheSeparatedOneFloatsIt()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);

        await Client.WaitAssertion(() =>
        {
            var ui = Client.Resolve<IUserInterfaceManager>();

            var overlay = new DefaultGameScreen();
            ui.WindowRoot.AddChild(overlay);
            var docked = overlay.ChatBox;
            Assert.That(docked.KsTguiActive, Is.True);
            Assert.That(docked.KsSayFloating, Is.False);
            Assert.That(docked.KsSay.Parent, Is.SameAs(docked.ChatInput.Parent), "docked under the output");
            Assert.That(docked.KsSay.Visible, Is.True);
            overlay.Orphan();

            var separated = new SeparatedChatGameScreen();
            ui.WindowRoot.AddChild(separated);
            var floating = separated.ChatBox;
            Assert.That(floating.KsSayFloating, Is.True);
            Assert.That(floating.KsSayWindow.Visible, Is.False, "there is no bar until chat is asked for");

            floating.Focus();
            Assert.That(floating.KsSayWindow.Parent, Is.SameAs(separated), "the window lives in the screen");
            Assert.That(floating.KsSayWindow.Visible, Is.True, "the focus keybinds open it");

            floating.KsSay.SetText("ks floating", notify: true);
            floating.KsSay.Submit();
            Assert.That(floating.KsSayWindow.Visible, Is.False, "sending closes it");
            Assert.That(floating.KsSay.Text, Is.Empty);

            floating.KsSayWindow.MoveTo(new System.Numerics.Vector2(-500f, -500f));
            Assert.That(floating.KsSayWindow.Position.X, Is.GreaterThanOrEqualTo(0f), "it can't be dragged off screen");

            separated.Orphan();
        });
    }

    [Test]
    public async Task TextTypedInTheBarGoesOutThroughTheStockPath()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);

        const string text = "ks tgui say send path";
        ChatUIController controller = default!;
        ChatBox box = default!;
        await Client.WaitPost(() =>
        {
            var ui = Client.Resolve<IUserInterfaceManager>();
            controller = ui.GetUIController<ChatUIController>();
            box = AddBox(ui.WindowRoot);
            box.KsSetTguiMode(true);

            box.ChatInput.ChannelSelector.Select(ChatSelectChannel.OOC);
            Assert.That(box.KsSay.ChannelLabel, Is.EqualTo(Loc.GetString("hud-chatbox-select-channel-OOC")), "the button names the channel");

            box.KsSay.SetText(text, notify: true);
            Assert.That(box.ChatInput.Input.Text, Is.EqualTo(text), "what is typed reaches the input the controller reads");

            box.KsSay.Submit();
            Assert.That(box.ChatInput.Input.Text, Is.Empty, "sending clears it");
            Assert.That(box.KsSay.Text, Is.Empty);
            Assert.That(box.KsSay.HistoryCount, Is.EqualTo(1));
        });

        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            Assert.That(controller.History.Any(entry => entry.Msg.Channel == ChatChannel.OOC && entry.Msg.Message.Contains(text)),
                Is.True, "the server said it back on OOC");
            box.Dispose();
        });
    }

    [Test]
    public async Task UpAndDownWalkTheLastFiveMessages()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);

        await Client.WaitPost(() =>
        {
            var bar = new KsTguiSayBar();
            for (var count = 1; count <= 7; count++)
                bar.AddToHistory($"message {count}");

            Assert.That(bar.HistoryCount, Is.EqualTo(5), "tgui keeps five");

            bar.SetText("half typed");
            bar.BrowseHistory(older: true);
            Assert.That(bar.Text, Is.EqualTo("message 7"));
            Assert.That(bar.HistoryPosition, Is.EqualTo(1), "the button shows where in the history it is");

            bar.BrowseHistory(older: true);
            Assert.That(bar.Text, Is.EqualTo("message 6"));

            for (var count = 0; count < 10; count++)
                bar.BrowseHistory(older: true);

            Assert.That(bar.Text, Is.EqualTo("message 3"), "it stops at the oldest");

            for (var count = 0; count < 10; count++)
                bar.BrowseHistory(older: false);

            Assert.That(bar.Text, Is.EqualTo("half typed"), "coming back gives the unsent text back");
            Assert.That(bar.HistoryPosition, Is.Zero);
        });
    }

    [Test]
    public async Task TheChannelButtonFollowsItsText()
    {
        await Client.WaitPost(() =>
        {
            var bar = new KsTguiSayBar();
            bar.SetChannel("OOC", Robust.Shared.Maths.Color.White);
            bar.Measure(Robust.Shared.Maths.Vector2Helpers.Infinity);
            var short_ = bar.DesiredSize.X;

            // A fresh bar: a measured one keeps its answer until the layout system invalidates it.
            var longBar = new KsTguiSayBar();
            longBar.SetChannel("Служба безопасности и охраны", Robust.Shared.Maths.Color.White);
            longBar.Measure(Robust.Shared.Maths.Vector2Helpers.Infinity);
            Assert.That(longBar.DesiredSize.X, Is.GreaterThan(short_ + 40f), "a long name widens the button instead of clipping");
        });
    }
}
