using Content.Server._KS14.Cultivation.Components;
using Content.Server.Pinpointer;
using Content.Server.Station.Systems;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     Heavenly treasures. Every fifteen to twenty minutes a spirit herb blooms somewhere on the station, and every
///         cultivator feels roughly where. Whoever reaches it first and digs it up gets a treasure: eaten, it fills a qi
///         cultivator's foundation, tempers a body cultivator, or simply heals a mortal. It can be carried, traded or
///         stolen, and if nobody claims it the bloom fades after a few minutes.
/// </summary>
public sealed partial class HeavenlyTreasureSystem : EntitySystem
{
    private static readonly TimeSpan FirstBloomMin = TimeSpan.FromMinutes(12);
    private static readonly TimeSpan FirstBloomMax = TimeSpan.FromMinutes(18);
    private static readonly TimeSpan BloomMin = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan BloomMax = TimeSpan.FromMinutes(20);
    /// <summary>
    ///     In Wuxia: Ascension heavenly treasures bloom a little more often.
    /// </summary>
    private const float AscensionBloomScale = 0.8f;

    private static readonly TimeSpan PetalInterval = TimeSpan.FromSeconds(3);

    [Dependency] private WuxiaAscensionRuleSystem _ascensionSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private CultivationLootSystem _lootSystem = default!;
    [Dependency] private LegendarySystem _legendarySystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private SharedBodyCultivationSystem _bodyCultivationSystem = default!;
    [Dependency] private SharedCultivationSystem _sharedCultivationSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private StationSystem _stationSystem = default!;
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private WuxiaWorldRuleSystem _ruleSystem = default!;

    /// <summary>
    ///     When the next treasure blooms. Zero until the cycle starts with the round.
    /// </summary>
    private TimeSpan _nextBloom;

    /// <summary>
    ///     When unclaimed legendary artifacts give themselves away to cultivators, as SS13 did eight and twenty minutes
    ///         into the round.
    /// </summary>
    private readonly List<TimeSpan> _omens = new();

    private static readonly TimeSpan[] OmenTimes = { TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(20) };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _nextBloom = TimeSpan.Zero;
        _omens.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Heaven only stirs in a wuxia round.
        if (!_ruleSystem.WuxiaActive())
        {
            _nextBloom = TimeSpan.Zero;
            _omens.Clear();
            return;
        }

        var now = _gameTiming.CurTime;
        if (_nextBloom == TimeSpan.Zero)
        {
            _nextBloom = now + _robustRandom.Next(FirstBloomMin, FirstBloomMax) * (_ascensionSystem.Active() ? AscensionBloomScale : 1f);
            foreach (var omenTime in OmenTimes)
                _omens.Add(now + omenTime);
        }
        else if (now >= _nextBloom)
        {
            _nextBloom = now + _robustRandom.Next(BloomMin, BloomMax) * (_ascensionSystem.Active() ? AscensionBloomScale : 1f);
            TryBloom();
        }

        if (_omens.Count > 0 && now >= _omens[0])
        {
            _omens.RemoveAt(0);
            _legendarySystem.Omen(Sensers());
        }

        var plants = EntityQueryEnumerator<SpiritHerbPlantComponent>();
        while (plants.MoveNext(out var uid, out var plantComponent))
        {
            if (now < plantComponent.NextPetals)
                continue;

            plantComponent.NextPetals = now + PetalInterval;
            _visualsSystem.Art(CultivationVisualsSystem.ArtPetals, uid);
        }
    }

    /// <summary>
    ///     Everyone who can feel a treasure bloom: living qi and body cultivators.
    /// </summary>
    public List<EntityUid> Sensers()
    {
        var sensers = new List<EntityUid>();
        var cultivators = EntityQueryEnumerator<CultivatorComponent>();
        while (cultivators.MoveNext(out var uid, out _))
        {
            if (_mobStateSystem.IsAlive(uid))
                sensers.Add(uid);
        }

        var bodies = EntityQueryEnumerator<BodyCultivatorComponent>();
        while (bodies.MoveNext(out var uid, out var bodyComponent))
        {
            if (bodyComponent.Committed && _mobStateSystem.IsAlive(uid) && !sensers.Contains(uid))
                sensers.Add(uid);
        }

        return sensers;
    }

    /// <summary>
    ///     A spirit herb blooms somewhere on a station, if anyone is there to feel it.
    /// </summary>
    public EntityUid? TryBloom()
    {
        var sensers = Sensers();
        if (sensers.Count == 0)
            return null;

        foreach (var stationUid in _stationSystem.GetStations())
        {
            if (_stationSystem.GetLargestGrid(stationUid) is not { } gridUid)
                continue;

            var spots = _lootSystem.MaintenanceSpots(gridUid);
            var spot = spots.Count > 0 ? _robustRandom.Pick(spots) : _lootSystem.RandomSpot(gridUid);
            if (spot is not { } coordinates)
                continue;

            var herbUid = Spawn("KsSpiritHerbPlant", coordinates);
            Announce(herbUid, sensers);
            return herbUid;
        }

        return null;
    }

    private void Announce(EntityUid herbUid, List<EntityUid> sensers)
    {
        var where = _noticeSystem.Where(herbUid);
        foreach (var senserUid in sensers)
        {
            _noticeSystem.Notify(senserUid, Loc.GetString("treasure-bloom", ("where", where)), PopupType.Large, alsoToChat: true);
            _audioSystem.PlayEntity(CultivationVisualsSystem.WindChimesSound, Filter.Entities(senserUid), senserUid, true);
        }

        _visualsSystem.Wave(herbUid, FxColour.Gold, big: true);
    }

    #region Picking

    [SubscribeLocalEvent]
    private void OnPlantInteractHand(Entity<SpiritHerbPlantComponent> entity, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _noticeSystem.Announce(entity, Loc.GetString("treasure-digging", ("user", args.User), ("target", entity.Owner)), type: PopupType.Small);
        _doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, entity.Comp.PickTime, new SpiritHerbPickDoAfterEvent(), entity, target: entity)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    [SubscribeLocalEvent]
    private void OnPlantPicked(Entity<SpiritHerbPlantComponent> entity, ref SpiritHerbPickDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || TerminatingOrDeleted(entity) || EntityManager.IsQueuedForDeletion(entity))
            return;

        args.Handled = true;
        var herbUid = Spawn(entity.Comp.Herb, Transform(entity).Coordinates);
        _handsSystem.PickupOrDrop(args.User, herbUid, checkActionBlocker: false);
        _visualsSystem.Play(CultivationVisualsSystem.GuqinRiseSound, args.User);
        _noticeSystem.Announce(args.User, Loc.GetString("treasure-plucked", ("user", args.User)), type: PopupType.Medium);
        foreach (var senserUid in Sensers())
        {
            if (senserUid != args.User)
                _noticeSystem.Notify(senserUid, Loc.GetString("treasure-claimed"), alsoToChat: true);
        }

        QueueDel(entity);
    }

    #endregion

    #region Eating

    [SubscribeLocalEvent]
    private void OnHerbUsed(Entity<SpiritHerbComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        Eat(entity, args.User);
    }

    /// <summary>
    ///     Claim the treasure's power: it heals anyone, and pours heavenly qi into a cultivator.
    /// </summary>
    public void Eat(Entity<SpiritHerbComponent> entity, EntityUid eaterUid)
    {
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtPetals, eaterUid);
        _visualsSystem.Wave(eaterUid, FxColour.Wood);
        _damageableSystem.HealEvenly(eaterUid, FixedPoint2.New(-entity.Comp.Heal));

        if (TryComp<CultivatorComponent>(eaterUid, out var cultivatorComponent))
        {
            _sharedCultivationSystem.HealInternalInjury((eaterUid, cultivatorComponent), entity.Comp.InjuryHeal);
            _sharedCultivationSystem.AddInsight((eaterUid, cultivatorComponent), entity.Comp.Insight);
            _sharedCultivationSystem.AddQi((eaterUid, cultivatorComponent), _sharedCultivationSystem.GetMaxQi(cultivatorComponent));
            _noticeSystem.Notify(eaterUid, Loc.GetString("treasure-eaten-cultivator"), PopupType.Large);
        }
        else if (TryComp<BodyCultivatorComponent>(eaterUid, out var bodyComponent) && bodyComponent.Committed)
        {
            _bodyCultivationSystem.AddTempering((eaterUid, bodyComponent), entity.Comp.Tempering);
            _noticeSystem.Notify(eaterUid, Loc.GetString("treasure-eaten-body"), PopupType.Large);
        }
        else
        {
            _noticeSystem.Notify(eaterUid, Loc.GetString("treasure-eaten-mortal"), PopupType.Medium);
        }

        QueueDel(entity);
    }

    #endregion
}
