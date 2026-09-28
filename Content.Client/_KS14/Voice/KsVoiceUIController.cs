using Content.Client._KS14.Voice.UI;
using Content.Client.Gameplay;
using Content.Shared._KS14.Voice;
using Content.Shared.Input;
using JetBrains.Annotations;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Input.Binding;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Voice;

/// <summary>
///     Push-to-talk keybind and the voice link window. Pressing push-to-talk without a microphone page connected opens
///         the window, which is how most players will find the feature.
/// </summary>
[UsedImplicitly]
public sealed partial class KsVoiceUIController : UIController, IOnStateChanged<GameplayState>, IOnSystemChanged<KsVoiceClientSystem>
{
    private static readonly TimeSpan CopiedFeedbackTime = TimeSpan.FromSeconds(2);

    [Dependency] private IUriOpener _uriOpener = default!;
    [Dependency] private IClipboardManager _clipboardManager = default!;
    [Dependency] private IInputManager _inputManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;

    [UISystemDependency] private readonly KsVoiceClientSystem? _voiceClientSystem = default!;

    private KsVoiceLinkWindow? _window;
    private string? _url;
    private string? _errorLocId;
    private TimeSpan? _copiedUntil;

    public void OnStateEntered(GameplayState state)
    {
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.KsVoicePushToTalk,
                InputCmdHandler.FromDelegate(
                    enabled: _ => OnPushToTalk(held: true),
                    disabled: _ => OnPushToTalk(held: false),
                    handle: false))
            .Register<KsVoiceUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        CommandBinds.Unregister<KsVoiceUIController>();
        _voiceClientSystem?.SetPushToTalk(false);
        _window?.Close();
    }

    public void OnSystemLoaded(KsVoiceClientSystem system)
    {
        system.LinkReceived += OnLinkReceived;
        system.StateChanged += OnStateChanged;
    }

    public void OnSystemUnloaded(KsVoiceClientSystem system)
    {
        system.LinkReceived -= OnLinkReceived;
        system.StateChanged -= OnStateChanged;
        _url = null;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_copiedUntil is { } until && _gameTiming.RealTime >= until)
        {
            _copiedUntil = null;
            _window?.SetCopyText(Loc.GetString("ks-voice-window-copy"));
        }
    }

    /// <summary>
    ///     Opens the voice link window, requesting the link from the server if we don't have it yet.
    /// </summary>
    public void OpenWindow()
    {
        if (_voiceClientSystem is not { Enabled: true })
            return;

        if (_window == null)
        {
            _window = UIManager.CreateWindow<KsVoiceLinkWindow>();
            _window.OnClose += () => _window = null;
            _window.OpenPressed += OnOpenPressed;
            _window.CopyPressed += OnCopyPressed;
            _window.ResetPressed += () => RequestLink(reset: true);
        }

        if (_url == null)
            RequestLink(reset: false);

        UpdateWindow();
        _window.OpenCentered();
    }

    private void OnPushToTalk(bool held)
    {
        if (_voiceClientSystem is not { Enabled: true } system)
            return;

        system.SetPushToTalk(held);

        if (held && !system.UplinkConnected && system.UplinkEnabled && _window == null)
            OpenWindow();
    }

    private void RequestLink(bool reset)
    {
        _url = null;
        _errorLocId = null;
        _voiceClientSystem?.RequestLink(reset);
        UpdateWindow();
    }

    private void OnLinkReceived(KsVoiceLinkEvent args)
    {
        _url = args.Url;
        _errorLocId = args.ErrorLocId;
        UpdateWindow();
    }

    private void OnStateChanged()
    {
        if (_voiceClientSystem is { Enabled: false })
        {
            _url = null;
            _window?.Close();
            return;
        }

        UpdateWindow();
    }

    private void OnOpenPressed()
    {
        if (_url != null)
            _uriOpener.OpenUri(_url);
    }

    private void OnCopyPressed()
    {
        if (_url == null || _window == null)
            return;

        _clipboardManager.SetText(_url);
        _window.SetCopyText(Loc.GetString("ks-voice-window-copied"));
        _copiedUntil = _gameTiming.RealTime + CopiedFeedbackTime;
    }

    private void UpdateWindow()
    {
        if (_window == null || _voiceClientSystem == null)
            return;

        _window.SetLinkAvailable(_url != null);
        _window.SetKeybind(Loc.GetString("ks-voice-window-keybind",
            ("key", _inputManager.GetKeyFunctionButtonString(ContentKeyFunctions.KsVoicePushToTalk))));

        if (_errorLocId != null)
            _window.SetStatus(Loc.GetString(_errorLocId));
        else if (_url == null)
            _window.SetStatus(Loc.GetString("ks-voice-window-loading"));
        else
            _window.SetStatus(Loc.GetString(_voiceClientSystem.UplinkConnected ? "ks-voice-window-connected" : "ks-voice-window-disconnected"));
    }
}
