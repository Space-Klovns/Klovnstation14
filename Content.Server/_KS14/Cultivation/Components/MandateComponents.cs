using Content.Server._KS14.Cultivation.Systems;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._KS14.Cultivation.Components;

/// <summary>
///     Heaven's Mandate over a department, or over the whole station for the Son of Heaven. Ported from SurfShack13's
///         mandate_of_heaven.dm and mandate_powers.dm; see <see cref="MandateSystem"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(MandateSystem))]
public sealed partial class MandateOfHeavenComponent : Component
{
    /// <summary>
    ///     The department ruled; null for the Son of Heaven, who rules everyone.
    /// </summary>
    [DataField]
    public ProtoId<DepartmentPrototype>? Domain;

    [DataField]
    public bool SonOfHeaven;

    /// <summary>
    ///     The job the Mandate came with; null if it was seized by raising a seal, and then no demotion takes it away.
    /// </summary>
    [DataField]
    public ProtoId<JobPrototype>? GrantedJob;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan HeldSince;

    /// <summary>
    ///     0-3, rising at 15, 30 and 60 minutes of unbroken rule.
    /// </summary>
    [DataField]
    public int DynastyTier;

    [DataField]
    public int TreasuryCharges = 1;

    [DataField]
    public bool TreasuryArtifactGiven;

    [DataField]
    public int PunishmentCharges = 1;

    [DataField]
    public TimeSpan RestrainedTime;

    [DataField]
    public TimeSpan DemotedTime;

    [DataField]
    public EntityUid? LastAttacker;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan LastAttacked;

    /// <summary>
    ///     When Heaven's Protection can turn aside a fatal blow again.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ProtectionReady;

    [DataField]
    public Dictionary<EntityUid, TimeSpan> RebukeCooldowns = new();

    [DataField]
    public HashSet<EntityUid> Nobles = new();

    [DataField]
    public HashSet<EntityUid> Guards = new();

    /// <summary>
    ///     Those who held this Mandate before, oldest first.
    /// </summary>
    [DataField]
    public List<string> Lineage = new();

    /// <summary>
    ///     The Imperial Court button.
    /// </summary>
    [DataField]
    public EntityUid? CourtAction;

    /// <summary>
    ///     When each power can be used again.
    /// </summary>
    [DataField]
    public Dictionary<Content.Shared._KS14.Cultivation.MandatePower, TimeSpan> ReadyAt = new();
}

/// <summary>
///     The jade seal a lost Mandate leaves behind. Whoever raises it to heaven rules in their place.
/// </summary>
[RegisterComponent, Access(typeof(MandateSystem))]
public sealed partial class JadeSealComponent : Component
{
    [DataField]
    public ProtoId<DepartmentPrototype>? Domain;

    [DataField]
    public bool SonOfHeaven;

    [DataField]
    public List<string> Lineage = new();

    [DataField]
    public TimeSpan ClaimTime = TimeSpan.FromSeconds(8);
}

/// <summary>
///     A noble title granted by the Son of Heaven. Cultivates faster; falls with the throne.
/// </summary>
[RegisterComponent, Access(typeof(MandateSystem))]
public sealed partial class ImperialNobleComponent : Component
{
    [DataField]
    public LocId Title = "mandate-title-baron";

    [DataField]
    public EntityUid Lord;
}

/// <summary>
///     A Jinyiwei, an imperial guard: warned whenever their lord is attacked, and cultivates a little faster.
/// </summary>
[RegisterComponent, Access(typeof(MandateSystem))]
public sealed partial class ImperialGuardComponent : Component
{
    [DataField]
    public EntityUid Lord;
}

/// <summary>
///     Marked by heaven for murdering a ruler: no seal will answer them.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(MandateSystem))]
public sealed partial class RegicideCurseComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan Until;
}

/// <summary>
///     Heaven's Punishment in progress: three bolts in succession.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(MandateSystem))]
public sealed partial class HeavensPunishmentComponent : Component
{
    [DataField]
    public int StrikesLeft = 3;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextStrike;
}
