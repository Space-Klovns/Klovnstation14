using Content.Server._KS14.Packet.Components;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared.NodeContainer;

namespace Content.Server._KS14.Packet.States;

public sealed class GasNodeState : DeviceCall
{
    public string NodeName = "pipe";

    public string[] Gas = [];
    public float[] Moles = [];
    public float Temperature { get; set; }
    public float TotalMoles  { get; set; }
    public float Pressure { get; set; }

    protected override void GetCall(EntityUid entity, PacketSystem packetSystem, PacketExecutorComponent executor)
    {
        if (!_entityManager.TryGetComponent<NodeContainerComponent>(entity, out var nodes))
            return;

        if (!nodes.Nodes.TryGetValue(NodeName, out var node) ||
            node is not PipeNode pipeNode)
            return;

        var gasEnum = new GasMixture.GasEnumerator(pipeNode.Air);

        while (gasEnum.MoveNext())
        {
            if (gasEnum.Current.moles == 0)
                continue;

            Gas[(int) gasEnum.Current.gas] = gasEnum.Current.gas.ToString();
            Moles[(int)gasEnum.Current.gas] = gasEnum.Current.moles;
        }

        Temperature = pipeNode.Air.Temperature;
        Pressure = pipeNode.Air.Pressure;
        TotalMoles = pipeNode.Air.TotalMoles;
    }
}
