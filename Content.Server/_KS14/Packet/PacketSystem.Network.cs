using System.Diagnostics.CodeAnalysis;
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
    private Dictionary<string, PacketNetwork> _networks = new();

    private string CreateNetwork(string[] addresses, int frequency)
    {
        var networkAddress = GenerateAddress();
        var entAddresses = new List<string>();

        foreach (var address in addresses)
        {
            if (!TryGetReceiver(frequency, address, out var receiver))
                continue;

            receiver.Comp.AddressNetwork = networkAddress;
            entAddresses.Add(receiver.Comp.Address);
        }

        _networks.Add(networkAddress, new PacketNetwork(frequency, entAddresses.ToArray()));

        return networkAddress;
    }

    public bool TryGetNetwork(string address, [NotNullWhen(returnValue: true)] out PacketNetwork? network)
    {
        return _networks.TryGetValue(address, out network);
    }
}
