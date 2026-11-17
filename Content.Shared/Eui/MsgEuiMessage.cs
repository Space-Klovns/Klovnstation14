using System.IO;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Content.Shared._KS14.Network; // KS14

namespace Content.Shared.Eui
{
    public sealed class MsgEuiMessage : NetMessage
    {
        public override MsgGroups MsgGroup => MsgGroups.Command;
        public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableOrdered;

        public uint Id;
        public EuiMessageBase Message = default!;

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer ser)
        {
            Id = buffer.ReadUInt32();

            var length = buffer.ReadVariableInt32();
            buffer.AssertAtLeastSize(length);
            KsNetMessageLimits.AssertAtMost(this, length, KsNetMessageLimits.MaxEuiMessageBytes, KsNetMessageLimits.MaxAdminEuiMessageBytes); // KS14: hard cap, stricter than upstream, looser for admins
            using var stream = new MemoryStream(length);
            buffer.ReadAlignedMemory(stream, length);
            Message = ser.Deserialize<EuiMessageBase>(stream);
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer ser)
        {
            buffer.Write(Id);
            var stream = new MemoryStream();

            ser.Serialize(stream, Message);
            var length = (int)stream.Length;
            buffer.WriteVariableInt32(length);
            buffer.Write(stream.GetBuffer().AsSpan(0, length));
        }
    }
}
