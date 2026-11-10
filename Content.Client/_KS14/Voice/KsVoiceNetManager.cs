using Content.Shared._KS14.Voice;
using Robust.Shared.Network;

namespace Content.Client._KS14.Voice;

/// <summary>
///     Registers the voice relay net message, which must exist before connecting, and hands received frames to
///         <see cref="KsVoicePlaybackSystem"/>.
/// </summary>
public sealed partial class KsVoiceNetManager
{
    [Dependency] private IClientNetManager _netManager = default!;

    /// <summary>
    ///     Raised on the main thread for every voice frame the server relays to us.
    /// </summary>
    public event Action<KsVoiceFrameMessage>? FrameReceived;

    public void Initialize()
    {
        _netManager.RegisterNetMessage<KsVoiceFrameMessage>(message => FrameReceived?.Invoke(message));
    }
}
