using System.Diagnostics.CodeAnalysis;
using Content.Server._KS14.Packet.Attributes;
using Content.Server._KS14.Packet.Components;
using Content.Shared._KS14.Packets.Widgets;

namespace Content.Server._KS14.Packet;

public sealed partial class PacketSystem
{
    private Dictionary<string, string> _referenceMap = [];

    private void InitReference(Entity<PacketNetworkComponent> ent)
    {
        if (!TryComp<PacketReferenceComponent>(ent, out var reference))
            return;

        _referenceMap.Add(reference.RefName, ent.Comp.Address);
    }

    private void FormatReferences(Entity<PacketExecutorComponent> executor)
    {
        foreach (var freq in _frequencies)
        {
            executor.Comp.Command = executor.Comp.Command.Replace(freq.Key.Id, freq.Value.ToString());
        }

        foreach (var reference in  _referenceMap)
        {
            executor.Comp.Command = executor.Comp.Command.Replace("REF_" + reference.Key, reference.Value);
        }
    }
}
