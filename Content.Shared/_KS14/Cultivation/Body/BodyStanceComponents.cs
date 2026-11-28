using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._KS14.Cultivation.Body;

/// <summary>
///     Thunderclap (everything forward) or Immovable Mountain (hold the line). Changes how the other arts work.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedBodyCultivationSystem))]
public sealed partial class BodyStanceComponent : Component
{
    [DataField, AutoNetworkedField]
    public BodyStance Stance;
}

/// <summary>
///     A timed body art that hardens or drives the body: Iron Shirt, Vajra Golden Body or Blood Boil.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(SharedBodyCultivationSystem))]
public sealed partial class BodyHardeningComponent : Component
{
    /// <summary>
    ///     Iron Shirt: brute damage multiplier while it lasts, or 1.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float BruteMultiplier = 1f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan IronShirtEnd;

    /// <summary>
    ///     Vajra Golden Body: brute and burn multiplier while it lasts.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float VajraMultiplier = 0.25f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan VajraEnd;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan BloodBoilEnd;

    [DataField]
    public float BloodBoilCrash = 40f;
}
