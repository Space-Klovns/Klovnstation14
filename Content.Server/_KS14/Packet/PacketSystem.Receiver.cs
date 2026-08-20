using Content.Server._KS14.Packet.Components;
using Content.Server._KS14.Packet.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet;

/// <summary>
/// Handles <see cref="PacketReceiverComponent"/> logic - Getting receivers, randomizing frequencies, etc.
/// </summary>
public sealed partial class PacketSystem
{
    /// <summary>
    /// Stores all packet receivers.
    /// String value is address.
    /// </summary>
    private Dictionary<string, Entity<PacketReceiverComponent>> _packetEntities = new();

    private void SetupAddress(Entity<PacketReceiverComponent> entity)
    {
        var value = _random.Next((int) Math.Pow(16, 6));
        entity.Comp.Address = "0x" + value.ToString("X");

        if (_packetEntities.ContainsKey(entity.Comp.Address))
        {
            SetupAddress(entity); // There is 1/10³⁵⁰⁰⁰⁰⁰ chance that this causes stack overflow btw.
            return;
        }

        _packetEntities.Add(entity.Comp.Address, entity);
    }

    private void RandomizeFrequencies()
    {
        var protoEnum = _protoMan.EnumeratePrototypes<PacketFrequencyPrototype>();

        foreach (var freqProto in protoEnum)
        {
            freqProto.Frequency = _random.Next(freqProto.MinimalFrequency, freqProto.MaximalFrequency);
        }
    }

    public bool TryGetReceiver(string address, out Entity<PacketReceiverComponent> receiver)
    {
        return _packetEntities.TryGetValue(address, out receiver) && Exists(receiver);
    }

    public bool TryGetReceiver(int freq, string address, out Entity<PacketReceiverComponent> receiver)
    {

        return _packetEntities.TryGetValue(address, out receiver) && Exists(receiver) && GetFrequency(receiver.Comp.Frequency) == freq;
    }

    public int GetFrequency(ProtoId<PacketFrequencyPrototype> freqProto)
    {
        return _protoMan.Index(freqProto).Frequency;
    }
}
