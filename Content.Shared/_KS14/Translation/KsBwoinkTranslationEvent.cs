using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Translation;

/// <summary>A replacement for one already authorized AHelp message, never a new chat notification.</summary>
[Serializable, NetSerializable]
public sealed class KsBwoinkTranslationEvent(NetUserId channel, Guid messageId, string text) : EntityEventArgs
{
    public NetUserId Channel { get; } = channel;
    public Guid MessageId { get; } = messageId;
    public string Text { get; } = text;
}
