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
    ///     How to use it: which powers are used in hand, which are action buttons, and so on.
    /// </summary>
    [DataField]
    public LocId? Controls;

    /// <summary>
    ///     Who has already been told how to use it.
    /// </summary>
    [DataField]
    public HashSet<EntityUid> Told = new();

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
    /// <summary>
    ///     Until when a second strike with the Heaven Reliant Sword clashes the two: the first only warns.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ClashArmedUntil;

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

/// <summary>
///     The one heaven has mandated, the Son of Heaven. The Xuanyuan Sword answers only to them.
/// </summary>
[RegisterComponent]
public sealed partial class SonOfHeavenComponent : Component;

/// <summary>
///     The Xuanyuan Sword: in the Son of Heaven's hands every blow staggers and knocks down; anyone else is burned.
/// </summary>
[RegisterComponent, Access(typeof(LegendarySystem))]
public sealed partial class XuanyuanSwordComponent : Component
{
    [DataField]
    public float WorthyDamage = 14f;

    [DataField]
    public float UnworthyBurn = 15f;
}

/// <summary>
///     The Green Dragon Crescent Blade: wielded, it reaches two tiles, and bites as hard as its wielder's honour.
/// </summary>
[RegisterComponent, Access(typeof(LegendarySystem))]
public sealed partial class GreenDragonBladeComponent : Component
{
    [DataField]
    public float WieldedReach = 2.5f;

    [DataField]
    public float UnwieldedReach = 1.5f;
}

/// <summary>
///     The Seven-Star Sword: every blow lights a star; with all seven lit, the Seven Stars pour out.
/// </summary>
[RegisterComponent, Access(typeof(LegendarySystem))]
public sealed partial class SevenStarSwordComponent : Component
{
    [DataField]
    public int Stars;

    public const int MaxStars = 7;

    /// <summary>
    ///     The fewest lit stars that can be released.
    /// </summary>
    public const int MinStarsToRelease = 3;

    /// <summary>
    ///     Stars still falling from the last release, in order.
    /// </summary>
    [ViewVariables]
    public List<SevenStarStrike> Falling = new();

    /// <summary>
    ///     Who has already been cut by one of the lesser stars of the current release.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> Struck = new();

    [ViewVariables]
    public EntityUid? Wielder;
}

/// <summary>
///     One star of a Seven Stars release: where and when it lands, and whether it's the great seventh star.
/// </summary>
public record struct SevenStarStrike(TimeSpan At, Robust.Shared.Map.MapCoordinates Point, bool Great);

/// <summary>
///     The Linglong Pagoda: thrown at someone, it swells over them and seals them inside.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(LegendarySystem))]
public sealed partial class LinglongPagodaComponent : Component
{
    public const string ContainerId = "ks-pagoda";

    [DataField]
    public EntityUid? Sealed;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ReleaseAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextSeal;

    [DataField]
    public TimeSpan SealTime = TimeSpan.FromSeconds(20);

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(90);
}

/// <summary>
///     Nezha's Universe Ring: thrown, it ricochets to two more people and flies home.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(LegendarySystem))]
public sealed partial class UniverseRingComponent : Component
{
    [DataField]
    public EntityUid? Thrower;

    [DataField]
    public int BouncesLeft;

    [ViewVariables]
    public HashSet<EntityUid> HitThisThrow = new();

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextFlight;

    [DataField]
    public bool Returning;

    [DataField]
    public float Damage = 12f;
}

/// <summary>
///     Nezha's Wind Fire Wheels: worn, they leave burning footprints, and they don't like to stop.
/// </summary>
[RegisterComponent, Access(typeof(LegendarySystem))]
public sealed partial class WindFireWheelsComponent : Component
{
    [DataField]
    public MapCoordinates? LastPosition;

    [DataField]
    public float SkidChance = 0.1f;
}

/// <summary>
///     Marks the six treasures whose blows <see cref="LegendarySystem"/> works out per treasure.
/// </summary>
[RegisterComponent]
public sealed partial class LegendaryTreasureHitComponent : Component;

/// <summary>
///     A legendary weapon's own mark: sparks of its colour thrown off whatever it hits.
/// </summary>
[RegisterComponent, Access(typeof(LegendarySystem))]
public sealed partial class LegendarySwingComponent : Component
{
    [DataField(required: true)]
    public Robust.Shared.Prototypes.EntProtoId HitEffect;
}

/// <summary>
///     A Pluck Hairs clone: it fights its master's target, and whoever its master strikes with the Ruyi.
/// </summary>
[RegisterComponent, Access(typeof(LegendarySystem))]
public sealed partial class SageCloneComponent : Component
{
    [DataField]
    public EntityUid Master;
}
