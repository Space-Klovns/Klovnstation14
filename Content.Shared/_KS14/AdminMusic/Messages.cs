using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.AdminMusic;

public sealed class KsAdminMusicDataMessage : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public byte EntryCount;
    public KsAdminMusicEntry[] Entries = default!;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        var count = buffer.ReadByte();
        if (count == 0)
        {
            Entries = [];
            return;
        }

        Entries = new KsAdminMusicEntry[count];
        for (var i = 0; i < count; i++)
            Entries[i] = new(new(buffer.ReadString()), buffer.ReadFloat(), buffer.ReadTimeSpan());
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(EntryCount);
        if (EntryCount == 0)
            return;

        foreach (var entry in Entries)
        {
            buffer.Write(entry.SoundPath.CanonPath);
            buffer.Write(entry.Volume);
            buffer.Write(entry.StartTime);
        }
    }

    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableOrdered;
}
