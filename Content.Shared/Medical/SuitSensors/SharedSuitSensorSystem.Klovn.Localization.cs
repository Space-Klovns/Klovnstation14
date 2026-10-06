using Content.Shared.Medical.SuitSensor;

namespace Content.Shared.Medical.SuitSensors;

public abstract partial class SharedSuitSensorSystem
{
    private static string? GetModeLocalizationId(SuitSensorMode mode) => mode switch
    {
        SuitSensorMode.SensorOff => "suit-sensor-mode-off",
        SuitSensorMode.SensorBinary => "suit-sensor-mode-binary",
        SuitSensorMode.SensorVitals => "suit-sensor-mode-vitals",
        SuitSensorMode.SensorCords => "suit-sensor-mode-cords",
        _ => null,
    };
}
