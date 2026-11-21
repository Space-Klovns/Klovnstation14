using Content.Shared._KS14.Cultivation.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._KS14.Cultivation.Components;

/// <summary>
///     Still Water Ward: incoming damage is softened until it runs out.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(CultivationStanceSystem))]
public sealed partial class StillWaterWardComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime;

    [DataField, AutoNetworkedField]
    public float DamageMultiplier = 0.5f;

    /// <summary>
    ///     Damage the ward has soaked up, released as a wave by a Water Root.
    /// </summary>
    [DataField]
    public float Stored;

    [DataField]
    public bool ReleaseWave;
}

/// <summary>
///     Rooted Mountain Stance: less damage taken, slower movement.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(CultivationStanceSystem))]
public sealed partial class RootedStanceComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime;

    [DataField, AutoNetworkedField]
    public float DamageMultiplier = 0.75f;

    [DataField, AutoNetworkedField]
    public float SpeedMultiplier = 0.6f;
}
