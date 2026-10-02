using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Meters;

/// <summary>
///     An NPC's meter readings, by meter. A reading is a value and when it was taken. The current value is worked out
///         from those and the meter's decay whenever it is asked for, so nothing has to tick meters along. A meter the
///         NPC has no reading for is at 0. See <see cref="NpcMeterSystem"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcMeterSystem))]
public sealed partial class NpcMetersComponent : Component
{
    [ViewVariables]
    public Dictionary<ProtoId<NpcMeterPrototype>, NpcMeterReading> Readings = new();
}

/// <param name="Value">The meter's value at <paramref name="TakenAt"/>.</param>
public readonly record struct NpcMeterReading(float Value, TimeSpan TakenAt);
