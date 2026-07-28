using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Chat;

/// <summary>
///     Sent from server to a single client to swap a previously delivered chat message in place with a
///     translated version. The client locates the original entry in its chat history by
///     <see cref="MessageId"/> and rebuilds the wrapped line locally (preserving any client-side
///     highlights/codewords). Only the translated plain text is carried; the wrapping is rebuilt client-side.
/// </summary>
public sealed class MsgReplaceChatMessage : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public int MessageId;
    public string Message = default!;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        MessageId = buffer.ReadInt32();
        Message = buffer.ReadString();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(MessageId);
        buffer.Write(Message);
    }
}
