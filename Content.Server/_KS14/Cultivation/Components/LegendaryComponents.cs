using Content.Server._KS14.Cultivation.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._KS14.Cultivation.Components;

/// <summary>
///     How a legendary artifact's blows answer the body path.
/// </summary>
[Flags]
public enum LegendaryTrait : byte
{
    None = 0,
    /// <summary>Cuts straight through Iron Shirt and the Vajra Golden Body.</summary>
    BreaksDefence = 1 << 0,
    /// <summary>Every blow knocks the breath out of a body cultivator.</summary>
    WearsDown = 1 << 1,
    /// <summary>The more tempered the body it strikes, the harder it lands.</summary>
    CrushesTempered = 1 << 2,
}

/// <summary>
///     A weapon or treasure out of legend. Its blows are true damage: no armour, no tempered flesh, no realm braces
///         against them. Each one answers the body path in its own way.
/// </summary>
[RegisterComponent, Access(typeof(LegendarySystem))]
public sealed partial class LegendaryArtifactComponent : Component
{
    [DataField]
    public LegendaryTrait Traits;

    /// <summary>
    ///     Its story, told to cultivators who examine it.
    /// </summary>
    [DataField(required: true)]
    public LocId Legend;

    /// <summary>
    ///     What its powers do, told to anyone who examines it.
    /// </summary>
    [DataField(required: true)]
    public LocId Power;

    /// <summary>
    ///     True damage each melee blow adds, on top of the weapon's own.
    /// </summary>
    [DataField]
    public float HitDamage;

    [DataField]
    public TimeSpan HitKnockdown = TimeSpan.Zero;

    /// <summary>
    ///     How far each blow hurls its target, if at all.
    /// </summary>
    [DataField]
    public float HitThrow;
}

/// <summary>
///     Ganjiang or Moye. Held together (by one person, or two standing side by side) they strike harder and can
///         unleash the Twin Dragon Sword Storm; apart, each tugs towards the other.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(LegendarySystem))]
public sealed partial class TwinSwordComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Partner;

    [DataField]
    public float PairedDamage = 12f;

    [DataField]
    public float StormDamage = 8f;

    [DataField]
    public int StormTicks = 10;

    [DataField]
    public TimeSpan StormInterval = TimeSpan.FromSeconds(0.5);

    [DataField]
    public TimeSpan StormCooldown = TimeSpan.FromSeconds(40);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextStorm;

    [DataField]
    public int StormTicksLeft;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextStormTick;

    [DataField]
    public EntityUid? Spinner;
}

/// <summary>
///     The Heaven Reliant Sword: cuts iron like mud, and looses a blade of light fourteen tiles long.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(LegendarySystem))]
public sealed partial class HeavenReliantComponent : Component
{
    [DataField]
    public TimeSpan CleaveCooldown = TimeSpan.FromSeconds(20);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextCleave;
}

/// <summary>
///     The Dragon Slaying Saber. Whoever holds it is First Under Heaven; woken, its next blow is a cataclysm.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(LegendarySystem))]
public sealed partial class DragonSaberComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan AwakeUntil;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextWake;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextClaim;

    [DataField]
    public TimeSpan WakeCooldown = TimeSpan.FromSeconds(30);

    [DataField]
    public TimeSpan AwakeDuration = TimeSpan.FromSeconds(10);
}

/// <summary>
///     The Monkey King's staff: a needle until it grows. Grown, it reaches three tiles, slams down from the heavens,
///         and planted upright only its master can lift it.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(LegendarySystem))]
public sealed partial class RuyiStaffComponent : Component
{
    [DataField]
    public bool Extended;

    /// <summary>
    ///     Who planted it upright, the only one who can lift it again.
    /// </summary>
    [DataField]
    public EntityUid? Planter;

    /// <summary>
    ///     Just let go of at full size: it stands up once it lands.
    /// </summary>
    [DataField]
    public bool PlantPending;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextSlam;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextHairs;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan SlamLandsAt;

    [DataField]
    public MapCoordinates? SlamLanding;

    [DataField]
    public EntityUid? SlamUser;

    [DataField]
    public EntProtoId Clone = "KsSageClone";
}

/// <summary>
///     The Purple-Gold Gourd. Whoever answers when its holder calls their name is sucked inside to slowly dissolve.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(LegendarySystem))]
public sealed partial class PurpleGoldGourdComponent : Component
{
    public const string ContainerId = "ks-gourd";

    [DataField]
    public EntityUid? Prisoner;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextCall;

    [DataField]
    public TimeSpan CallCooldown = TimeSpan.FromSeconds(60);

    [DataField]
    public float CallRange = 9f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ReleaseAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextDissolve;

    [DataField]
    public TimeSpan HoldTime = TimeSpan.FromMinutes(2);

    [DataField]
    public TimeSpan EscapeTime = TimeSpan.FromSeconds(45);

    [DataField]
    public float DissolveDamage = 1.5f;

    [DataField]
    public EntProtoId Elixir = "KsGourdElixir";

    [DataField]
    public bool Escaping;
}

/// <summary>
///     Someone whose name a Purple-Gold Gourd has just called. If they answer before it lapses, they're inside.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(LegendarySystem))]
public sealed partial class GourdCalledComponent : Component
{
    [DataField]
    public EntityUid Gourd;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ListenUntil;
}

/// <summary>
///     What is left of someone the gourd dissolved. Grim, and extraordinarily potent.
/// </summary>
[RegisterComponent, Access(typeof(LegendarySystem))]
public sealed partial class GourdElixirComponent : Component
{
    [DataField]
    public string DistilledFrom = string.Empty;
}

/// <summary>
///     The Plantain Fan: one wave sends a hurricane down the corridor.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(LegendarySystem))]
public sealed partial class PlantainFanComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextGust;

    [DataField]
    public TimeSpan GustCooldown = TimeSpan.FromSeconds(15);
}

/// <summary>
///     The Bagua Mirror: its flash seals the cultivation of everyone who sees it, and burns the wicked.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(LegendarySystem))]
public sealed partial class BaguaMirrorComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextFlash;

    [DataField]
    public TimeSpan FlashCooldown = TimeSpan.FromSeconds(45);

    [DataField]
    public TimeSpan SealDuration = TimeSpan.FromSeconds(20);
}

/// <summary>
///     The Qiankun Pouch: a whole world inside. It swallows everything loose around it and hurls it all back out,
///         and a willing person can hide in its world for a while.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(LegendarySystem))]
public sealed partial class QiankunPouchComponent : Component
{
    public const string WorldContainerId = "ks-qiankun-world";

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextSwallow;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextRelease;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(20);

    [DataField]
    public EntityUid? Guest;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan GuestUntil;

    [DataField]
    public TimeSpan HideTime = TimeSpan.FromSeconds(30);
}

/// <summary>
///     A manual holding a technique nobody else on the station can learn. One reader takes it, and then the pages crumble.
/// </summary>
[RegisterComponent, Access(typeof(LegendarySystem))]
public sealed partial class InheritanceManualComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Action;

    [DataField(required: true)]
    public LocId Learned;

    [DataField]
    public bool Used;
}
