using Content.Server._KS14.Cultivation.Systems;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._KS14.Cultivation.Components;

// Short-lived state the martial arts leave on whoever they touch. Only MartialArtsSystem writes it.

/// <summary>
///     Nine Swords of Dugu: waiting for the opening in the next blow.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(MartialArtsSystem))]
public sealed partial class DuguCounterComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan EndTime;

    [DataField]
    public float CounterDamage = 15f;

    [DataField]
    public TimeSpan Knockdown = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Only blows from this close can be read and broken.
    /// </summary>
    [DataField]
    public float Range = 2f;
}

/// <summary>
///     Great Shift of Heaven and Earth: incoming projectiles are turned back on whoever loosed them.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(MartialArtsSystem))]
public sealed partial class GreatShiftComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan EndTime;

    [DataField]
    public int Charges = 3;
}

/// <summary>
///     Struck by the Eighteen Dragon-Subduing Palms: each further palm from the same hand hits harder.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(MartialArtsSystem))]
public sealed partial class DragonPalmComboComponent : Component
{
    [DataField]
    public EntityUid? Striker;

    [DataField]
    public int Stacks;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan Expires;
}
