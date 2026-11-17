using System.IO;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Content.Shared._KS14.Network; // KS14

namespace Content.Shared.Eui
{
    public sealed class MsgEuiState : NetMessage
    {
        public override MsgGroups MsgGroup => MsgGroups.Command;
        public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableOrdered;

        public uint Id;
        public EuiStateBase State = default!;

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer ser)
        {
            KsNetMessageLimits.AssertNotReceivedByServer(this); // KS14: only the server sends this
            Id = buffer.ReadUInt32();

            var length = buffer.ReadVariableInt32();
            buffer.AssertAtLeastSize(length);
            KsNetMessageLimits.AssertAtMost(length, KsNetMessageLimits.MaxEuiStateBytes); // KS14: hard cap, stricter than upstream
            using var stream = new MemoryStream(length);
            buffer.ReadAlignedMemory(stream, length);
            State = ser.Deserialize<EuiStateBase>(stream);
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer ser)
        {
            buffer.Write(Id);
            var stream = new MemoryStream();

            ser.Serialize(stream, State);
            var length = (int)stream.Length;
            buffer.WriteVariableInt32(length);
            buffer.Write(stream.GetBuffer().AsSpan(0, length));
        }
    }
}
