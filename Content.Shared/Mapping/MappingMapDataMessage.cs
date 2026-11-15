using System.IO;
using System.Text; // KS14
using Content.Shared._KS14.Network; // KS14
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.Mapping;

public sealed class MappingMapDataMessage : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;
    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableUnordered;

    public ZStdCompressionContext Context = default!;
    public string Yml = default!;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        KsNetMessageLimits.AssertNotReceivedByServer(this); // KS14: only the server sends this
        MsgSize = buffer.LengthBytes;

        var uncompressedLength = buffer.ReadVariableInt32();
        KsNetMessageLimits.AssertAtMost(uncompressedLength, KsNetMessageLimits.MaxMapDataUncompressedBytes); // KS14: hard cap, stricter than upstream

        var compressedLength = buffer.ReadVariableInt32();
        buffer.AssertAtLeastSize(compressedLength);
        KsNetMessageLimits.AssertAtMost(compressedLength, KsNetMessageLimits.MaxMapDataCompressedBytes); // KS14: hard cap, stricter than upstream

        var stream = new MemoryStream(compressedLength);
        buffer.ReadAlignedMemory(stream, compressedLength);
        using var decompress = new ZStdDecompressStream(stream);
        // KS14 start: the declared length is the sender's word. Don't allocate it up front, and stop
        //      decompressing as soon as the data turns out longer than declared.
        /* using var decompressed = new MemoryStream(uncompressedLength); */
        using var decompressed = new MemoryStream();

        /* decompress.CopyTo(decompressed, uncompressedLength); */
        KsNetMessageLimits.CopyAtMost(decompress, decompressed, uncompressedLength);
        // KS14 end

        // KS14 start: the YAML travels as plain UTF-8 instead of through the serializer. NetSerializer 1.1.1
        //      reads at most 64 MiB of strings in one call, at two bytes a character, and our largest maps
        //      are longer than that.
        /*
        decompressed.Position = 0;
        serializer.DeserializeDirect(decompressed, out Yml);
        */
        Yml = Encoding.UTF8.GetString(decompressed.GetBuffer(), 0, (int) decompressed.Length);
        // KS14 end
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        // KS14 start: plain UTF-8, see ReadFromBuffer.
        /*
        var stream = new MemoryStream();
        serializer.SerializeDirect(stream, Yml);
        buffer.WriteVariableInt32((int) stream.Length);

        stream.Position = 0;
        var buf = new byte[ZStd.CompressBound((int) stream.Length)];
        var length = Context.Compress2(buf, stream.AsSpan());
        */
        var ymlBytes = Encoding.UTF8.GetBytes(Yml);
        buffer.WriteVariableInt32(ymlBytes.Length);

        var buf = new byte[ZStd.CompressBound(ymlBytes.Length)];
        var length = Context.Compress2(buf, ymlBytes);
        // KS14 end

        buffer.WriteVariableInt32(length);
        buffer.Write(buf.AsSpan(0, length));
    }
}
