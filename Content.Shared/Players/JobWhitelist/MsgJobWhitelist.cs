using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Content.Shared._KS14.Network; // KS14

namespace Content.Shared.Players.JobWhitelist;

public sealed class MsgJobWhitelist : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.EntityEvent;

    public HashSet<string> Whitelist = new();

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        KsNetMessageLimits.AssertNotReceivedByServer(this); // KS14: only the server sends this
        var count = buffer.ReadVariableInt32();
        buffer.AssertAtLeastSize(count * NetMessageExt.MinSizeOfString);
        KsNetMessageLimits.AssertAtMost(count, KsNetMessageLimits.MaxPrototypeListEntries); // KS14: hard cap, stricter than upstream
        Whitelist.EnsureCapacity(count);

        for (var i = 0; i < count; i++)
        {
            Whitelist.Add(buffer.ReadString());
        }
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.WriteVariableInt32(Whitelist.Count);

        foreach (var ban in Whitelist)
        {
            buffer.Write(ban);
        }
    }
}
