using Content.Server._KS14.Packet.Components;
using Content.Server._KS14.Packet.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet;

/// <summary>
/// Handles <see cref="PacketNetworkComponent"/> logic - Getting receivers, randomizing frequencies, etc.
/// </summary>
public sealed partial class PacketSystem
{
    /// <summary>
    /// Stores all packet receivers.
    /// String value is address.
    /// </summary>
    private Dictionary<string, Entity<PacketNetworkComponent>> _packetEntities = new();

    private void SetupAddress(Entity<PacketNetworkComponent> entity)
    {
        entity.Comp.Address =  GenerateAddress();
        _packetEntities.Add(entity.Comp.Address, entity);
    }

    private string GenerateAddress()
    {
        var value = _random.Next((int) Math.Pow(16, 6));
        var address = "0x" + value.ToString("X");

        if (_packetEntities.ContainsKey(address))
        {
            return GenerateAddress(); // There is 1/10³⁵⁰⁰⁰⁰⁰ chance that this causes stack overflow btw.
        }
        return address;
    }

    private void RandomizeFrequencies()
    {
        var protoEnum = _prototypeManager.EnumeratePrototypes<PacketFrequencyPrototype>();

        foreach (var freqProto in protoEnum)
        {
            freqProto.Frequency = _random.Next(freqProto.MinimalFrequency, freqProto.MaximalFrequency);
        }
    }

    public bool TryGetReceiver(string address, out Entity<PacketNetworkComponent> receiver)
    {
        return _packetEntities.TryGetValue(address, out receiver) && Exists(receiver);
    }

    public bool TryGetReceiver(int freq, string address, out Entity<PacketNetworkComponent> receiver)
    {

        return _packetEntities.TryGetValue(address, out receiver) && Exists(receiver) && GetFrequency(receiver.Comp.Frequency) == freq;
    }

    public int GetFrequency(ProtoId<PacketFrequencyPrototype> freqProto)
    {
        return _prototypeManager.Index(freqProto).Frequency;
    }
}
