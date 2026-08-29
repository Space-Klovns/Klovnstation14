using System.Linq;
using Content.Server._KS14.Packet.Components;
using Content.Server._KS14.Packet.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

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
    private Dictionary<ProtoId<PacketFrequencyPrototype>, int> _frequencies = new();

    private void SetupAddress(Entity<PacketNetworkComponent> entity)
    {
        entity.Comp.Address =  GenerateAddress();
        _packetEntities.Add(entity.Comp.Address, entity);
    }

    private void ReloadFrequencies(Entity<PacketNetworkComponent> entity)
    {
        entity.Comp.ListeningFrequencies.Clear();
        var freq = _prototypeManager.Index(entity.Comp.Frequency);

        foreach (var listFreq in freq.ListeningFrequencies)
        {
            entity.Comp.ListeningFrequencies.Add(listFreq);
        }
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
            _frequencies.Add(freqProto, freqProto.Frequency);
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

    public bool TryGetReceiver(Entity<PacketNetworkComponent?> sender, int freq, string address, out Entity<PacketNetworkComponent> receiver)
    {
        if (!(_packetEntities.TryGetValue(address, out receiver) && Exists(receiver) && GetFrequency(receiver.Comp.Frequency) == freq))
            return false;

        return ValidateReceiver(receiver, sender);
    }

    public bool ValidateReceiver(Entity<PacketNetworkComponent> receiver, Entity<PacketNetworkComponent?> sender)
    {
        return sender.Comp is { } && receiver.Comp.ListeningFrequencies.Contains(sender.Comp.Frequency);
    }

    public int GetFrequency(ProtoId<PacketFrequencyPrototype> freqProto)
    {
        return _frequencies.TryGetValue(freqProto, out var freq) ? freq : 0;
    }

    public ProtoId<PacketFrequencyPrototype> GetFrequency(int freqProto)
    {
        return _frequencies.FirstOrDefault(key => key.Value == freqProto).Key;
    }
}
