using Content.Server._KS14.Cultivation.Systems;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._KS14.Cultivation.Components;

/// <summary>
///     A cultivator walking the Demonic Path. A master holds the scripture in their heart and can transmit it; a
///         disciple only received it and can never pass it on. Every forbidden art leaves taint behind, and taint
///         makes a breakthrough harder until it fades.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(ForbiddenArtsSystem))]
public sealed partial class DemonicPathComponent : Component
{
    [DataField]
    public bool Master;

    [DataField]
    public float Taint;

    /// <summary>
    ///     Taint that fades each minute.
    /// </summary>
    [DataField]
    public float TaintDecay = 2f;

    /// <summary>
    ///     The most breakthrough readiness taint can cost.
    /// </summary>
    [DataField]
    public int MaxReadinessPenalty = 30;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextDecay;
}

/// <summary>
///     A gu worm raised on its master's blood, sipping its host's health into the master's qi.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(ForbiddenArtsSystem))]
public sealed partial class GuWormComponent : Component
{
    [DataField]
    public EntityUid? Master;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ExpiresAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextSip;
}

/// <summary>
///     Palms that struck the Five-Point Exploding Heart lately. Spiritual Sense smells it.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(ForbiddenArtsSystem))]
public sealed partial class ExplodingHeartStainComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ExpiresAt;
}

/// <summary>
///     A corpse strung up with demonic qi. The real body rides inside and drops out when the qi runs out.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(ForbiddenArtsSystem))]
public sealed partial class JiangshiComponent : Component
{
    public const string CorpseContainer = "jiangshi_corpse";

    [DataField]
    public EntityUid? Master;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ExpiresAt;
}

/// <summary>
///     Remembers the one jiangshi a demonic cultivator keeps at a time.
/// </summary>
[RegisterComponent, Access(typeof(ForbiddenArtsSystem))]
public sealed partial class JiangshiMasterComponent : Component
{
    [DataField]
    public EntityUid? Puppet;
}

/// <summary>
///     A corpse with nothing left in it to devour.
/// </summary>
[RegisterComponent, Access(typeof(ForbiddenArtsSystem))]
public sealed partial class DevouredComponent : Component;

/// <summary>
///     A forbidden scripture. Only those already sworn against the station can bear to read it; anyone else's qi
///         deviates. A master's transmission is written for one disciple and makes them a disciple, not a master.
/// </summary>
[RegisterComponent, Access(typeof(ForbiddenArtsSystem))]
public sealed partial class DemonicScriptureComponent : Component
{
    /// <summary>
    ///     Whether reading it makes a disciple (a master's transmission) instead of a master (the scripture itself).
    /// </summary>
    [DataField]
    public bool Transmission;

    [DataField]
    public TimeSpan ReadDuration = TimeSpan.FromSeconds(12);

    /// <summary>
    ///     Whatever lived in the book has already moved into someone's heart.
    /// </summary>
    [DataField]
    public bool Spent;
}
