using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.TTS;

/// <summary>
///     Used to lazily transmit TTS speech.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
[UnsavedComponent]
public sealed partial class TtsAudioComponent : Component
{
    [AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public byte[] Bytes = [];
}

[Serializable, NetSerializable]
public sealed class PlayTtsEvent : EntityEventArgs
{
    public NetEntity Source;
    public byte[] Data;

    public PlayTtsEvent(NetEntity source, byte[] data)
    {
        Source = source;
        Data = data;
    }
}
