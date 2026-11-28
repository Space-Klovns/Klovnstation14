using Content.Shared._KS14.Cultivation.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._KS14.Cultivation.Components;

// Timed effects techniques leave on a body. CultivationStanceSystem applies and expires them all, so movement
// changes are predicted.

/// <summary>
///     Held in place or slowed: Realm Pressure, Binding Vines, Acupoint Sealing, sucking mud.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(CultivationStanceSystem))]
public sealed partial class CultivationSnaredComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime;

    [DataField, AutoNetworkedField]
    public float SpeedMultiplier = 0.6f;
}

/// <summary>
///     Moving faster than flesh should: Burning Blood Essence, Sword Riding.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(CultivationStanceSystem))]
public sealed partial class CultivationHasteComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime;

    [DataField, AutoNetworkedField]
    public float SpeedMultiplier = 1.3f;
}

/// <summary>
///     Golden Bell Qi: for a few seconds nothing gets through.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(CultivationStanceSystem))]
public sealed partial class GoldenBellQiComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime;
}

/// <summary>
///     Dharma Idol: a towering golden Buddha stands behind you. You loom larger and blows land softer.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(CultivationStanceSystem))]
public sealed partial class DharmaIdolComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime;

    [DataField, AutoNetworkedField]
    public float DamageMultiplier = 0.6f;
}

/// <summary>
///     Turtle Breathing: no breath needed at all.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(CultivationStanceSystem))]
public sealed partial class TurtleBreathingComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime;
}

/// <summary>
///     Concealment Art: cultivation drawn so deep inside that others see a mortal. Using any technique reveals it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CultivationConcealedComponent : Component;

/// <summary>
///     A wall of a barrier array: its maker and their friends pass through freely.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class QiBarrierComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Maker;
}
