using Content.Server._KS14.Packet.Attributes;
using Content.Server._KS14.Packet.Components;
using Content.Server.Chemistry.Components;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet.States;

public sealed class ChemMasterState : DeviceCall
{
    [PacketField(PacketFieldType.Returns)]
    public ProtoId<ReagentPrototype>[] BeakerReagents = [];
    [PacketField(PacketFieldType.Returns)]
    public ProtoId<ReagentPrototype>[] BufferReagents = [];
    [PacketField(PacketFieldType.Returns)]
    public float[] BeakerQuantity = [];
    [PacketField(PacketFieldType.Returns)]
    public float[] BufferQuantity = [];

    protected override void GetCall(EntityUid entity, PacketSystem packetSystem, EntityManager entityMan, IPrototypeManager protoMan, PacketExecutorComponent executor)
    {
        if (!entityMan.TryGetComponent<SolutionComponent>(entity, out var solution))
            return;

        for (var i = 0; i < solution.Solution.Contents.Count; i++)
        {
            BufferQuantity[i] = solution.Solution.Contents[i].Quantity.Float();
            BufferReagents[i] = solution.Solution.Contents[i].Reagent.Prototype;
        }

        if (!packetSystem.ContainerSystem.TryGetContainer(entity, "beakerSlot", out var container)
            || container is not ContainerSlot slot
            || slot.ContainedEntity is not { } beaker
            || !entityMan.TryGetComponent<SolutionComponent>(beaker, out var beakerSolution))
            return;

        for (var i = 0; i < beakerSolution.Solution.Contents.Count; i++)
        {
            BeakerQuantity[i] = beakerSolution.Solution.Contents[i].Quantity.Float();
            BeakerReagents[i] = beakerSolution.Solution.Contents[i].Reagent.Prototype;
        }
    }
}
