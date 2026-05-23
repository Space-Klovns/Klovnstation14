namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     Holds data for "sensor events", to be processed by NPCs
///         via <see cref="HTN.PrimitiveTasks.Operators.HandleSensorsOperator"/>.
///
///     These are
/// </summary>
[RegisterComponent]
public sealed partial class NpcSensorsComponent : Component
{
    public Dictionary<string, object?> AggregatedEffects = [];
}


public record struct SensorEventDatum(string Id, object? Value);
