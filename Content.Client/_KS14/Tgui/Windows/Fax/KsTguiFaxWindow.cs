using System.Linq;
using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.UserInterface.Controls;
using Content.Shared.Fax;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Windows.Fax;

/// <summary>
///     What <see cref="Content.Client.Fax.UI.FaxBoundUi"/> needs from either fax window.
/// </summary>
public interface IKsFaxWindow
{
    event Action? FileButtonPressed;
    event Action? CopyButtonPressed;
    event Action? SendButtonPressed;
    event Action? RefreshButtonPressed;
    event Action<string>? PeerSelected;

    bool OfficePaper { get; }

    void UpdateState(FaxUiState state);
}

/// <summary>
///     The fax machine laid out after tgui's Fax interface: About, Send (a button per recipient, not a dropdown) and a
///         section for printing a file.
/// </summary>
public sealed class KsTguiFaxWindow : FancyWindow, IKsFaxWindow
{
    public event Action? FileButtonPressed;
    public event Action? CopyButtonPressed;
    public event Action? SendButtonPressed;
    public event Action? RefreshButtonPressed;
    public event Action<string>? PeerSelected;

    public bool OfficePaper { get; private set; }

    private readonly KsTguiLabeledListItem _fromItem;
    private readonly KsTguiLabeledListItem _paperItem;
    private readonly KsTguiNoticeBox _noPeersNotice;
    private readonly BoxContainer _peerBox;
    private readonly KsTguiButton _copyButton;
    private readonly KsTguiButton _sendButton;
    private readonly KsTguiButton _paperTypeButton;
    private readonly KsTguiButton _fileButton;
    private readonly KsTguiButton _refreshButton;

    public KsTguiFaxWindow()
    {
        Title = Loc.GetString("fax-machine-ui-window");
        MinSize = new Vector2(340f, 0f);
        SetSize = new Vector2(340f, 400f);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = KsTguiSection.Gap,
            Margin = new Thickness(6f),
            VerticalExpand = true,
        };

        var about = new KsTguiSection { Title = Loc.GetString("ks-tgui-fax-about") };
        var aboutList = new KsTguiLabeledList();
        _fromItem = aboutList.AddItem(Loc.GetString("fax-machine-ui-from"));
        _paperItem = aboutList.AddItem(Loc.GetString("fax-machine-ui-paper"));
        about.AddChild(aboutList);
        root.AddChild(about);

        var send = new KsTguiSection { Title = Loc.GetString("fax-machine-ui-to"), Fill = true, Scrollable = true };
        _refreshButton = new KsTguiButton
        {
            Icon = KsTguiIcons.Sync,
            Compact = true,
            ToolTip = Loc.GetString("fax-machine-ui-refresh-button"),
        };
        _refreshButton.OnPressed += _ => RefreshButtonPressed?.Invoke();
        send.Buttons = _refreshButton;

        var sendBody = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        _noPeersNotice = new KsTguiNoticeBox { Text = Loc.GetString("fax-machine-ui-no-peers"), NoticeColor = KsTguiNoticeColor.Warning };
        sendBody.AddChild(_noPeersNotice);
        _peerBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        sendBody.AddChild(_peerBox);

        var actions = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _copyButton = new KsTguiButton { Text = Loc.GetString("fax-machine-ui-copy-button"), Fluid = true, HorizontalExpand = true, Disabled = true };
        _sendButton = new KsTguiButton { Text = Loc.GetString("fax-machine-ui-send-button"), Fluid = true, HorizontalExpand = true, Disabled = true, Color = KsTguiColor.Green };
        _copyButton.OnPressed += _ => CopyButtonPressed?.Invoke();
        _sendButton.OnPressed += _ => SendButtonPressed?.Invoke();
        actions.AddChild(_copyButton);
        actions.AddChild(_sendButton);
        sendBody.AddChild(actions);
        send.AddChild(sendBody);
        root.AddChild(send);

        var print = new KsTguiSection { Title = Loc.GetString("fax-machine-ui-file-button") };
        var printBody = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _paperTypeButton = new KsTguiButton { Text = Loc.GetString("fax-machine-ui-paper-button-normal"), Fluid = true, HorizontalExpand = true };
        _paperTypeButton.OnPressed += _ => TogglePaperType();
        _fileButton = new KsTguiButton { Text = Loc.GetString("fax-machine-ui-file-button"), Fluid = true, HorizontalExpand = true };
        _fileButton.OnPressed += _ => FileButtonPressed?.Invoke();
        printBody.AddChild(_paperTypeButton);
        printBody.AddChild(_fileButton);
        print.AddChild(printBody);
        root.AddChild(print);

        ContentsContainer.AddChild(root);
    }

    /// <summary>
    ///     For tests: presses the button of the given recipient.
    /// </summary>
    public IEnumerable<KsTguiButton> PeerButtons => _peerBox.Children.OfType<KsTguiButton>();

    public KsTguiButton CopyButton => _copyButton;
    public KsTguiButton SendButton => _sendButton;
    public KsTguiButton PaperTypeButton => _paperTypeButton;
    public KsTguiButton FileButton => _fileButton;
    public KsTguiButton RefreshButton => _refreshButton;

    private void TogglePaperType()
    {
        OfficePaper = !OfficePaper;
        _paperTypeButton.Text = Loc.GetString(OfficePaper
            ? "fax-machine-ui-paper-button-office"
            : "fax-machine-ui-paper-button-normal");
    }

    public void UpdateState(FaxUiState state)
    {
        _copyButton.Disabled = !state.CanCopy;
        _sendButton.Disabled = !state.CanSend;
        _fromItem.Text = state.DeviceName;

        _paperItem.Text = Loc.GetString(state.IsPaperInserted
            ? "fax-machine-ui-paper-inserted"
            : "fax-machine-ui-paper-not-inserted");
        _paperItem.TextColor = state.IsPaperInserted ? KsTguiPalettes.Good : KsTguiPalettes.Bad;

        // There must always be a destination.
        if (string.IsNullOrEmpty(state.DestinationAddress) && state.AvailablePeers.Count != 0)
        {
            foreach (var address in state.AvailablePeers.Keys)
            {
                PeerSelected?.Invoke(address);
                return;
            }
        }

        _noPeersNotice.Visible = state.AvailablePeers.Count == 0;
        _peerBox.RemoveAllChildren();

        foreach (var (address, name) in state.AvailablePeers)
        {
            var peerAddress = address;
            var peerButton = new KsTguiButton
            {
                Text = name,
                Fluid = true,
                Selected = address == state.DestinationAddress,
                ToolTip = name,
            };
            peerButton.OnPressed += _ => PeerSelected?.Invoke(peerAddress);
            _peerBox.AddChild(peerButton);
        }
    }
}
