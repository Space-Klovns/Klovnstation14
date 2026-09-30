using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Voice;
using Robust.Shared.Configuration;

namespace Content.Client._KS14.Voice;

/// <summary>
///     Client end of the voice control channel: push-to-talk, requesting the personal microphone link, and whether a
///         microphone page is connected. Audio playback lives in <see cref="KsVoicePlaybackSystem"/>.
/// </summary>
public sealed partial class KsVoiceClientSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;

    private bool _pushToTalkHeld;
    private bool _voiceActivationAllowed;
    private bool _voiceActivationSetting;

    /// <summary>
    ///     Whether the server has voice chat enabled.
    /// </summary>
    public bool Enabled { get; private set; }

    /// <summary>
    ///     Whether the server accepts microphone pages right now.
    /// </summary>
    public bool UplinkEnabled { get; private set; }

    /// <summary>
    ///     Whether this player currently has a microphone page connected.
    /// </summary>
    public bool UplinkConnected { get; private set; }

    /// <summary>
    ///     Whether this player talks without holding push-to-talk: their setting, if the server allows it.
    /// </summary>
    public bool VoiceActivation => _voiceActivationAllowed && _voiceActivationSetting;

    public event Action<KsVoiceLinkEvent>? LinkReceived;
    public event Action? StateChanged;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.VoiceEnabled, OnEnabledChanged, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceUplinkEnabled, value =>
        {
            UplinkEnabled = value;
            StateChanged?.Invoke();
        }, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceActivationAllowed, value =>
        {
            _voiceActivationAllowed = value;
            StateChanged?.Invoke();
        }, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceActivation, value =>
        {
            _voiceActivationSetting = value;
            StateChanged?.Invoke();
        }, invokeImmediately: true);
    }

    public void SetPushToTalk(bool held)
    {
        held &= Enabled;
        if (_pushToTalkHeld == held)
            return;

        _pushToTalkHeld = held;
        RaiseNetworkEvent(new KsVoicePushToTalkEvent(held));
    }

    public void RequestLink(bool reset)
    {
        if (Enabled)
            RaiseNetworkEvent(new KsVoiceRequestLinkEvent(reset));
    }

    private void OnEnabledChanged(bool enabled)
    {
        Enabled = enabled;
        if (!enabled)
        {
            _pushToTalkHeld = false;
            UplinkConnected = false;
        }

        StateChanged?.Invoke();
    }

    [SubscribeNetworkEvent]
    private void OnLink(KsVoiceLinkEvent args)
    {
        LinkReceived?.Invoke(args);
    }

    [SubscribeNetworkEvent]
    private void OnUplinkStatus(KsVoiceUplinkStatusEvent args)
    {
        UplinkConnected = args.Connected;
        StateChanged?.Invoke();
    }
}
