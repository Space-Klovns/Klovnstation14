using Content.Server._KS14.Packet.Attributes;
using Content.Server._KS14.Packet.Components;
using Content.Server.Botany.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet.States;

public sealed class HydroTrayState : DeviceCall
{
    [PacketField(PacketFieldType.Returns)]
    public string SeedName = "none";
    [PacketField(PacketFieldType.Returns)]
    public float Potency { get; set; }
    [PacketField(PacketFieldType.Returns)]
    public int Yield { get; set; }
    [PacketField(PacketFieldType.Returns)]
    public float WaterLevel  { get; set; }
    [PacketField(PacketFieldType.Returns)]
    public float NutritionLevel  { get; set; }
    [PacketField(PacketFieldType.Returns)]
    public int Age  { get; set; }
    [PacketField(PacketFieldType.Returns)]
    public float Health  { get; set; }
    [PacketField(PacketFieldType.Returns)]
    public bool Dead  { get; set; }

    protected override void GetCall(EntityUid entity, PacketSystem packetSystem, EntityManager entityMan, IPrototypeManager protoMan, PacketExecutorComponent executor)
    {
        if (!entityMan.TryGetComponent<PlantHolderComponent>(entity, out var ame))
            return;

        WaterLevel = ame.WaterLevel;
        NutritionLevel = ame.NutritionLevel;
        Age = ame.Age;
        Health = ame.Health;
        Dead = ame.Dead;

        if (ame.Seed is not { } seed)
            return;

        SeedName = seed.Name;
        Potency = seed.Potency;
        Yield = seed.Yield;
    }
}
