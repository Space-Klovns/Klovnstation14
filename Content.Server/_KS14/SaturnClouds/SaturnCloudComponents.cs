namespace Content.Server._KS14.SaturnClouds;

/// <summary>
///     Marks a map whose grids are exposed to Saturn's winds.
/// </summary>
[RegisterComponent, Access(typeof(SaturnCloudSystem))]
public sealed partial class SaturnCloudMapComponent : Component
{

    [DataField(required: true)]
    public TimeSpan UpdateInterval;

    [DataField(required: true)]
    public TimeSpan DamageDelay;

    [DataField(required: true)]
    public TimeSpan DamageInterval;

    [DataField(required: true)]
    public TimeSpan DestructionDelay;

    [DataField(required: true)]
    public TimeSpan DestructionWarningLeadTime;

    [DataField(required: true)]
    public int InitialDamageRadius;

    [DataField(required: true)]
    public TimeSpan DamageRadiusIncreaseInterval;

    [DataField(required: true)]
    public int MaximumDamageRadius;

    [ViewVariables]
    public TimeSpan LastUpdate;

    [ViewVariables]
    public TimeSpan NextUpdate;
}

/// <summary>
///     Marks the station grid whose loss ends a cloud round.
/// </summary>
[RegisterComponent, Access(typeof(SaturnCloudSystem))]
public sealed partial class SaturnMainStationGridComponent : Component;

/// <summary>
///     Tracks how long a grid has lacked working mooring.
/// </summary>
[RegisterComponent, Access(typeof(SaturnCloudSystem))]
public sealed partial class SaturnWindExposedComponent : Component
{
    [ViewVariables]
    public TimeSpan UnprotectedTime;

    [ViewVariables]
    public TimeSpan NextDamageTime;

    [ViewVariables]
    public bool DestructionWarningAnnounced;
}

/// <summary>
///     Marks a non-station grid as naturally resistant to the winds on a Saturn cloud map.
/// </summary>
[RegisterComponent, Access(typeof(SaturnCloudSystem))]
public sealed partial class InnateMooringComponent : Component;

/// <summary>
///     A powered machine which protects its current grid from wind damage.
/// </summary>
[RegisterComponent, Access(typeof(SaturnCloudSystem))]
public sealed partial class MooringDeviceComponent : Component
{
    [DataField(required: true)]
    public float[] WarningLevels = [];

    [ViewVariables]
    public int WarningIndex;

    [ViewVariables]
    public bool WasOperational = true;

    /// <summary>
    ///     The main grid this device was mapped onto. Moving onto a split-off grid disables it.
    /// </summary>
    [ViewVariables]
    public EntityUid? ProtectedGrid;
}
