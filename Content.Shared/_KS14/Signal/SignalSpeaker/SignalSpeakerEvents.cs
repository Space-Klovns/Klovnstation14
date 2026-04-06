using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Signal.SignalSpeaker;

/// <summary>
/// Key representing which <see cref="PlayerBoundUserInterface"/> is currently open.
/// Useful when there are multiple UI for an object. Here it's future-proofing only.
/// </summary>
[Serializable, NetSerializable]
public enum SignalSpeakerUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class SignalSpeakerTextChangedMessage(string text) : BoundUserInterfaceMessage
{
    public string Text { get; } = text;
}

[Serializable, NetSerializable]
public sealed class SignalSpeakerApplyMessage : BoundUserInterfaceMessage
{
}
