using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Atmos;

/// <summary>
///     The gas a thermomachine is working on, for the Status section of its tgui window.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsGasThermomachineStatusState(float temperature, float pressure) : BoundUserInterfaceState, IEquatable<KsGasThermomachineStatusState>
{
    public float Temperature { get; } = temperature;

    public float Pressure { get; } = pressure;

    // The bound user interface sends a state only when it isn't equal to the last, which a machine at rest always is.
    public bool Equals(KsGasThermomachineStatusState? other)
    {
        return other != null && Temperature.Equals(other.Temperature) && Pressure.Equals(other.Pressure);
    }

    public override bool Equals(object? obj)
    {
        return obj is KsGasThermomachineStatusState other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Temperature, Pressure);
    }
}
