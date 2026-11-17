#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Chat;
using Content.Client._KS14.Tgui.Tooltips;
using Content.Client.Communications.UI;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Client.UserInterface;
using Content.Client.UserInterface.Systems.Chat;
using Content.Client.UserInterface.Systems.Chat.Controls;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Client.Gameplay;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.Chat;
using Content.Shared.Input;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The first round of live-server fixes to the tgui theme: dropdowns that changed look, jumped, and left a closing copy at
///         the wrong scale; tooltips that blinked out; the say bar's height, channel menu and focus bug.
/// </summary>
public sealed class KsTguiLiveFixesTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private IUserInterfaceManager UiManager => Client.ResolveDependency<IUserInterfaceManager>();

    private KsTguiFeelUIController Feel => UiManager.GetUIController<KsTguiFeelUIController>();

    private async Task SetTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);
    }

    private static void Press(BaseButton button)
    {
        var field = typeof(BaseButton).GetField(nameof(BaseButton.OnPressed), BindingFlags.Instance | BindingFlags.NonPublic);
        var handler = (Action<BaseButton.ButtonEventArgs>?)field!.GetValue(button);
        handler?.Invoke(new BaseButton.ButtonEventArgs(button, null!));
    }

    private static void Restyle(Control control)
    {
        control.ForceRunStyleUpdate();
        foreach (var child in control.Children)
            Restyle(child);
    }

    private OptionButton MakeOption(Vector2 position)
    {
        var option = new OptionButton();
        option.AddItem("one");
        option.AddItem("two");
        UiManager.PopupRoot.AddChild(option);
        Restyle(option);
        option.Measure(new Vector2(300f, 300f));
        option.Arrange(UIBox2.FromDimensions(position, option.DesiredSize));
        return option;
    }

    private Popup OpenDropdown(OptionButton option)
    {
        UiManager.SetHovered(option);
        Press(option);
        return UiManager.ModalRoot.Children.OfType<Popup>().Single(found => found.HasStyleClass(OptionButton.StyleClassPopup));
    }

    private static void AssertEntriesTagged(Popup popup)
    {
        var buttons = Descendants(popup).OfType<Button>().ToList();
        Assert.That(buttons, Is.Not.Empty);
        foreach (var button in buttons)
        {
            Assert.That(button.HasStyleClass(KsTguiSheetlet.StyleClassDropdownEntry),
                $"The entry '{button.Text}' lost the dropdown look");
        }
    }

    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var inner in Descendants(child))
                yield return inner;
        }
    }

    /// <summary>
    ///     An option button that is cleared and filled again while its menu is open (the comms console does, every refresh)
    ///         made plain buttons, which the general button rules paint blue.
    /// </summary>
    [Test]
    public async Task EntriesAddedToAnOpenDropdownKeepTheDropdownLook()
    {
        await SetTheme(true);

        await Client.WaitPost(() =>
        {
            var option = MakeOption(new Vector2(20f, 20f));
            var popup = OpenDropdown(option);
            AssertEntriesTagged(popup);

            option.Clear();
            option.AddItem("three");
            option.AddItem("four");
            option.AddItem("five");

            AssertEntriesTagged(popup);
            Assert.That(Descendants(popup).OfType<Button>().Count(), Is.EqualTo(3));

            popup.Close();
            option.Orphan();
        });
    }

    private static readonly string[] AlertLevels = ["green", "blue", "red"];

    /// <summary>
    ///     The console refreshes every five seconds with the same levels, and rebuilding the entries of the menu under the
    ///         player's cursor is what flashed them.
    /// </summary>
    [Test]
    public async Task CommsAlertEntriesAreNotRebuiltByARefreshThatChangesNothing()
    {
        await Client.WaitPost(() =>
        {
            var menu = new CommunicationsConsoleMenu();
            menu.UpdateAlertLevels([..AlertLevels], "green");

            var box = (BoxContainer)typeof(OptionButton)
                .GetField("_popupContentsBox", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(menu.AlertLevelButton)!;
            var before = box.Children.ToList();
            Assert.That(before, Has.Count.EqualTo(3));

            menu.UpdateAlertLevels([..AlertLevels], "green");
            Assert.That(box.Children.ToList(), Is.EqualTo(before), "An unchanged refresh must leave the entries alone");

            menu.UpdateAlertLevels([..AlertLevels], "blue");
            Assert.That(box.Children.ToList(), Is.Not.EqualTo(before), "A new level is a real change");
            Assert.That(menu.AlertLevelButton.SelectedMetadata, Is.EqualTo("blue"));
        });
    }

    /// <summary>
    ///     A closing copy out of the tree has no UI scale to ask, so it was drawn at the scale-1 position and size.
    /// </summary>
    [Test]
    public async Task AClosingDropdownIsDrawnWhereTheOpenOneWasAtAScaledUi()
    {
        await SetTheme(true);
        await OverrideCVar(Side.Client, CVars.DisplayUIScale, 1.5f);
        await Pair.RunTicksSync(3);

        OptionButton option = null!;
        Popup popup = null!;
        KsTguiScaleHost host = null!;
        UIBox2i openRect = default;
        await Client.WaitPost(() =>
        {
            Assert.That(UiManager.RootControl.UIScale, Is.GreaterThan(1.2f), "The scale must really be on");
            option = MakeOption(new Vector2(20f, 20f));
            popup = OpenDropdown(option);
        });

        await Pair.RunTicksSync(30);

        await Client.WaitPost(() =>
        {
            host = (KsTguiScaleHost)popup.Children.Single();
            Assert.That(host.UIScale, Is.EqualTo(UiManager.RootControl.UIScale));
            openRect = UIBox2i.FromDimensions(host.GlobalPixelPosition, host.PixelSize);
            Assert.That(openRect.Size.X, Is.GreaterThan(0));

            popup.Close();
            Assert.That(Feel.DropdownGhosts, Has.Count.EqualTo(1));
        });

        await Pair.RunTicksSync(1);

        await Client.WaitAssertion(() =>
        {
            var ghost = Feel.DropdownGhosts.Single();
            Assert.That(host.Parent, Is.SameAs(ghost), "The menu is held in the tree while it closes");
            Assert.That(host.UIScale, Is.EqualTo(UiManager.RootControl.UIScale), "so it has the real UI scale");

            var closingRect = UIBox2i.FromDimensions(host.GlobalPixelPosition, host.PixelSize);
            Assert.That(closingRect.Left, Is.EqualTo(openRect.Left).Within(1));
            Assert.That(closingRect.Top, Is.EqualTo(openRect.Top).Within(1));
            Assert.That(closingRect.Width, Is.EqualTo(openRect.Width).Within(1));
            Assert.That(closingRect.Height, Is.EqualTo(openRect.Height).Within(1));

            option.Orphan();
        });
    }

    [Test]
    public async Task ADropdownNearTheBottomOpensAboveItsButton()
    {
        await SetTheme(true);

        await Client.WaitPost(() =>
        {
            var modalRoot = UiManager.ModalRoot;
            var option = MakeOption(new Vector2(20f, modalRoot.Size.Y - 30f));
            var popup = OpenDropdown(option);

            var host = (KsTguiScaleHost)popup.Children.Single();
            var origin = PopupContainer.GetPopupOrigin(popup);
            var height = popup.DesiredSize.Y;
            Assert.That(origin.Y + height, Is.LessThanOrEqualTo(option.GlobalPosition.Y - KsTguiFeelUIController.DropdownMenuGap + 0.01f),
                "It opens above the button, 6px clear of it");
            Assert.That(host.PivotBottom, Is.True, "and scales about its bottom edge");
            popup.Close();

            var high = MakeOption(new Vector2(20f, 20f));
            var below = OpenDropdown(high);
            Assert.That(PopupContainer.GetPopupOrigin(below).Y,
                Is.EqualTo(high.GlobalPosition.Y + high.Size.Y + KsTguiFeelUIController.DropdownMenuGap).Within(0.01f),
                "With room below it opens there");
            Assert.That(((KsTguiScaleHost)below.Children.Single()).PivotBottom, Is.False);
            below.Close();

            option.Orphan();
            high.Orphan();
        });
    }

    /// <summary>
    ///     The engine sets the menu 1px below and shows it. The place is already the final one on that very call, not a frame
    ///         later.
    /// </summary>
    [Test]
    public async Task ADropdownIsPlacedOnTheCallThatOpensIt()
    {
        await SetTheme(true);

        await Client.WaitPost(() =>
        {
            var option = MakeOption(new Vector2(20f, 20f));
            var popup = OpenDropdown(option);

            Assert.That(PopupContainer.GetPopupOrigin(popup).Y,
                Is.EqualTo(option.GlobalPosition.Y + option.Size.Y + KsTguiFeelUIController.DropdownMenuGap).Within(0.01f),
                "Before any frame has run");

            popup.Close();
            option.Orphan();
        });
    }

    private async Task<(DefaultWindow Window, Button Button)> OpenWindowWithButton()
    {
        await SetTheme(true);

        DefaultWindow window = null!;
        Button button = null!;
        await Client.WaitPost(() =>
        {
            window = new DefaultWindow { SetSize = new Vector2(400f, 300f) };
            button = new Button { Text = "button", ToolTip = "a tooltip", HorizontalAlignment = Control.HAlignment.Center, VerticalAlignment = Control.VAlignment.Top };
            window.Contents.AddChild(button);
            window.OpenCentered();
        });
        await Pair.RunTicksSync(2);
        return (window, button);
    }

    /// <summary>
    ///     The engine hides a tooltip by taking it out of its host in the middle of the hover update. The host was then laid out
    ///         at nothing before the controller put the tooltip back, so one frame drew no tooltip.
    /// </summary>
    [Test]
    public async Task ATooltipTakenOutByTheEngineKeepsItsSizeAndStartsClosing()
    {
        var (window, button) = await OpenWindowWithButton();

        await Client.WaitPost(() =>
        {
            var tooltip = new Tooltip { Text = button.ToolTip, Tracking = true };
            UiManager.PopupRoot.AddChild(tooltip);
            Feel.TakeTooltip(tooltip, button);
            Feel.UpdateWindowTooltips(0f);
            Feel.UpdateWindowTooltips(0.3f);
            var host = (KsTguiTooltipHost)tooltip.Parent!;
            host.Measure(Vector2Helpers.Infinity);
            var fullSize = host.DesiredSize;
            Assert.That(fullSize.X, Is.GreaterThan(10f));

            // The engine hides it, and the next thing to run is layout.
            tooltip.Orphan();
            host.InvalidateMeasure();
            host.Measure(Vector2Helpers.Infinity);

            Assert.That(host.DesiredSize, Is.EqualTo(fullSize), "No frame may see an empty host");
            Assert.That(tooltip.Parent, Is.SameAs(host));
            Assert.That(host.Closing, Is.True, "and it is already closing");
            Assert.That(host.Modulate.A, Is.GreaterThan(0.9f));

            window.Close();
        });
    }

    private async Task<ChatBox> AddTguiBox()
    {
        await SetTheme(true);
        ChatBox box = null!;
        await Client.WaitPost(() =>
        {
            box = new ChatBox();
            UiManager.WindowRoot.AddChild(box);
            box.SetWidth = 400f;
            box.KsSetTguiMode(true);
        });
        await Pair.RunTicksSync(3);
        return box;
    }

    /// <summary>
    ///     The text box's scroll bar has a minimum size of its own, taller than a whole line.
    /// </summary>
    [Test]
    public async Task TheSayBarIsOneLineTallAndGrowsByALine()
    {
        await SetTheme(true);

        KsTguiSayBar bar = null!;
        await Client.WaitPost(() =>
        {
            bar = new KsTguiSayBar();
            bar.SetWidth = KsTguiSayBar.WindowWidth;
            UiManager.WindowRoot.AddChild(bar);
        });
        await Pair.RunTicksSync(4);

        await Client.WaitAssertion(() =>
        {
            bar.Measure(new Vector2(KsTguiSayBar.WindowWidth, float.PositiveInfinity));
            Assert.That(bar.DesiredSize.Y, Is.EqualTo(KsTguiSayBar.SmallHeight), "One line");
            Assert.That(bar.ScrollBarShown, Is.False);

            bar.SetText("first\nsecond");
        });
        await Pair.RunTicksSync(4);

        await Client.WaitAssertion(() =>
        {
            bar.Measure(new Vector2(KsTguiSayBar.WindowWidth, float.PositiveInfinity));
            Assert.That(bar.DesiredSize.Y, Is.EqualTo(KsTguiSayBar.MediumHeight), "Two lines");
            bar.Orphan();
        });
    }

    private static void Check(Action action)
    {
        Assert.DoesNotThrow(() => action());
    }

    /// <summary>
    ///     The key that opened the bar arrives as text. Undoing it from inside the change event left the cursor past the end of
    ///         the text: no caret, and every key after it threw.
    /// </summary>
    [Test]
    public async Task TheKeyThatOpenedTheBarIsDroppedAndTypingGoesOn()
    {
        await SetTheme(true);

        KsTguiSayBar bar = null!;
        await Client.WaitPost(() =>
        {
            bar = new KsTguiSayBar();
            bar.SetWidth = KsTguiSayBar.WindowWidth;
            UiManager.WindowRoot.AddChild(bar);
            bar.FocusInput();
            Check(() => bar.Edit.InsertAtCursor("t"));
        });
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            Assert.That(bar.Text, Is.Empty, "The opening key's character is gone");
            Assert.That(bar.Edit.CursorPosition.Index, Is.InRange(0, bar.Edit.TextLength), "and the cursor is inside the text");

            Check(() => bar.Edit.InsertAtCursor("h"));
            Check(() => bar.Edit.InsertAtCursor("i"));
        });
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            Assert.That(bar.Text, Is.EqualTo("hi"), "Typing right after is kept");
            Assert.That(bar.Edit.CursorPosition.Index, Is.EqualTo(2));
            bar.Orphan();
        });
    }

    [Test]
    public async Task APasteOverTheLengthLimitIsCutAndTypingGoesOn()
    {
        var box = await AddTguiBox();
        var controller = UiManager.GetUIController<ChatUIController>();
        var limit = controller.MaxMessageLength;

        await Client.WaitPost(() =>
        {
            Check(() => box.KsSay.Edit.InsertAtCursor(new string('a', limit + 25)));
        });
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            Assert.That(box.KsSay.Text, Has.Length.EqualTo(limit), "Cut to the limit");
            Assert.That(box.KsSay.Edit.CursorPosition.Index, Is.EqualTo(limit), "with the cursor at the end of it");

            box.KsSay.SetText("short");
            Check(() => box.KsSay.Edit.InsertAtCursor("!"));
        });
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            Assert.That(box.KsSay.Text, Is.EqualTo("short!"));
            box.Orphan();
        });
    }

    private static void PressChannelButton(ChatBox box)
    {
        Press((BaseButton)box.KsSay.ChannelButton);
    }

    /// <summary>
    ///     Clicking the channel button lists every channel the player can pick, each with its key, and picking one selects it
    ///         and puts the cursor back in the bar.
    /// </summary>
    [Test]
    public async Task TheChannelButtonOpensAMenuOfChannelsWithTheirKeys()
    {
        var box = await AddTguiBox();
        var controller = UiManager.GetUIController<ChatUIController>();

        KsTguiChannelMenu menu = null!;
        ChatSelectChannel other = default;
        await Client.WaitPost(() =>
        {
            UiManager.SetHovered(box.KsSay.ChannelButton);
            PressChannelButton(box);
            menu = UiManager.ModalRoot.Children.OfType<KsTguiChannelMenu>().Single();

            var expected = ChannelSelectorPopup.ChannelSelectorOrder
                .Where(channel => (controller.SelectableChannels & channel) != 0).ToList();
            Assert.That(menu.Rows.Keys, Is.EqualTo(expected), "One row per channel the player can pick, in the selector's order");
            Assert.That(expected, Has.Count.GreaterThan(1));

            foreach (var (channel, row) in menu.Rows)
            {
                var text = KsTguiChannelMenu.ShortcutsText(channel);
                if (ChatUIController.ChannelPrefixes.TryGetValue(channel, out var prefix) && prefix != default)
                    Assert.That(text, Does.Contain(prefix.ToString()), $"{channel} shows its prefix");

                var function = channel switch
                {
                    ChatSelectChannel.Local => ContentKeyFunctions.FocusLocalChat,
                    ChatSelectChannel.Whisper => ContentKeyFunctions.FocusWhisperChat,
                    ChatSelectChannel.Emotes => ContentKeyFunctions.FocusEmote,
                    ChatSelectChannel.Radio => ContentKeyFunctions.FocusRadio,
                    ChatSelectChannel.LOOC => ContentKeyFunctions.FocusLOOC,
                    ChatSelectChannel.OOC => ContentKeyFunctions.FocusOOC,
                    ChatSelectChannel.Dead => ContentKeyFunctions.FocusDeadChat,
                    _ => ContentKeyFunctions.FocusAdminChat,
                };

                if (BoundKeyHelper.TryGetShortKeyName(function, out var key))
                    Assert.That(text, Does.EndWith(key), $"{channel} shows its key");

                Assert.That(row.HasStyleClass(KsTguiSheetlet.StyleClassDropdownEntry), $"{channel}'s row has the dropdown look");
            }

            other = menu.Rows.Keys.First(channel => channel != box.SelectedChannel);
            Press(menu.Rows[other]);

            Assert.That(box.SelectedChannel, Is.EqualTo(other), "Picking a row selects its channel");
            Assert.That(box.KsSay.HasInputFocus, Is.True, "and the cursor goes back to the bar");
            Assert.That(menu.Visible, Is.False, "The menu closes");
            box.Orphan();
        });
    }

    /// <summary>
    ///     The docked bar is at the bottom of the screen, so its menu has to open upwards.
    /// </summary>
    [Test]
    public async Task TheChannelMenuOpensAboveABarAtTheBottom()
    {
        var box = await AddTguiBox();

        await Client.WaitPost(() =>
        {
            var modalRoot = UiManager.ModalRoot;
            box.Orphan();
            var bar = new KsTguiSayBar { HorizontalAlignment = Control.HAlignment.Left };
            UiManager.PopupRoot.AddChild(bar);
            bar.Measure(new Vector2(KsTguiSayBar.WindowWidth, 100f));
            bar.Arrange(UIBox2.FromDimensions(new Vector2(10f, modalRoot.Size.Y - bar.DesiredSize.Y), bar.DesiredSize));

            var menu = new KsTguiChannelMenu();
            menu.OpenBeside(bar.ChannelButton, ChatSelectChannel.OOC);

            Assert.That(PopupContainer.GetPopupOrigin(menu).Y + menu.DesiredSize.Y,
                Is.LessThanOrEqualTo(bar.ChannelButton.GlobalPosition.Y), "Above the button");

            menu.Close();
            bar.Orphan();
        });
    }
}

/// <summary>
///     Leaving the lobby unloads its screen before the lobby state shuts down, so unmarking the lobby chat as main found no chat
///         box and left it main for good, where focus could land on a chat box that is no longer on screen.
/// </summary>
public sealed class KsTguiLobbyChatMainTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, InLobby = true, Dirty = true };

    [Test]
    public async Task OnlyTheGameScreensChatIsMainAfterTheLobby()
    {
        var states = Client.ResolveDependency<IStateManager>();
        var uiManager = Client.ResolveDependency<IUserInterfaceManager>();

        ChatBox lobbyChat = null!;
        await Client.WaitPost(() =>
        {
            lobbyChat = ((LobbyState)states.CurrentState).Lobby!.Chat;
            Assert.That(lobbyChat.Main, Is.True, "The lobby's chat is the main one while in the lobby");
            states.RequestStateChange<GameplayState>();
        });
        await Pair.RunTicksSync(15);

        await Client.WaitAssertion(() =>
        {
            var gameChat = uiManager.ActiveScreen switch
            {
                Content.Client.UserInterface.Screens.DefaultGameScreen defaultScreen => defaultScreen.ChatBox,
                Content.Client.UserInterface.Screens.SeparatedChatGameScreen separated => separated.ChatBox,
                _ => throw new Exception($"Unexpected screen {uiManager.ActiveScreen}"),
            };
            Assert.That(gameChat, Is.Not.SameAs(lobbyChat));
            Assert.That(gameChat.Main, Is.True, "The game screen's chat is main");
            Assert.That(lobbyChat.Main, Is.False, "and the lobby's no longer is");
        });
    }
}
