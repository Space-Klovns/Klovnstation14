using Content.Server._KS14.Cultivation.Components;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Station.Systems;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     The world of the jianghu. Cultivation only touches a round while this rule runs: when it starts, the cultivation
///         loot is hidden about every station and wuxia weapons become craftable; while it runs, spirit herbs bloom and
///         unclaimed legendaries give themselves away. The wuxia gamemode starts it with the round; in any other round
///         it starts as soon as someone becomes a cultivator (an admin making one, say).
/// </summary>
public sealed partial class WuxiaWorldRuleSystem : GameRuleSystem<WuxiaWorldRuleComponent>
{
    public static readonly EntProtoId WorldRule = "KsWuxiaWorld";

    [Dependency] private WuxiaAscensionRuleSystem _ascensionSystem = default!;
    [Dependency] private CultivationLootSystem _lootSystem = default!;
    [Dependency] private StationSystem _stationSystem = default!;
    [Dependency] private MandateSystem _mandateSystem = default!;

    /// <summary>
    ///     A cultivator turned up this tick; the world wakes on the next update rather than in the middle of their
    ///         spawning.
    /// </summary>
    private bool _wakePending;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CultivatorComponent, ComponentStartup>((_, _, _) => _wakePending = true);
        SubscribeLocalEvent<BodyCultivatorComponent, ComponentStartup>((_, _, _) => _wakePending = true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_wakePending)
            return;

        _wakePending = false;
        if (GameTicker.RunLevel == GameRunLevel.InRound && !WuxiaActive())
            GameTicker.StartGameRule(WorldRule);
    }

    protected override void Started(EntityUid uid, WuxiaWorldRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        // Only the first world rule of a round hides loot; starting it again doesn't double it.
        var rules = QueryActiveRules();
        while (rules.MoveNext(out var otherUid, out _, out _, out _))
        {
            if (otherUid != uid)
                return;
        }

        foreach (var stationUid in _stationSystem.GetStations())
        {
            EnsureComp<WuxiaRoundComponent>(stationUid);
            if (_stationSystem.GetLargestGrid(stationUid) is { } gridUid)
                _lootSystem.SpawnLoot(gridUid, bountiful: _ascensionSystem.Active());
        }
        // Heaven grants its Mandate to the heads of staff already aboard.
        _mandateSystem.GrantExistingHeads();

        // Everyone already aboard has a standing in the jianghu they can examine themselves for.
        var bodies = EntityQueryEnumerator<Content.Shared.Humanoid.HumanoidProfileComponent, Robust.Shared.Player.ActorComponent>();
        while (bodies.MoveNext(out var bodyUid, out _, out _))
            EnsureComp<JianghuExamineComponent>(bodyUid);
    }

    protected override void Ended(EntityUid uid, WuxiaWorldRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);
        if (WuxiaActive())
            return;

        foreach (var stationUid in _stationSystem.GetStations())
            RemComp<WuxiaRoundComponent>(stationUid);
    }

    /// <summary>
    ///     Whether the jianghu is awake this round.
    /// </summary>
    public bool WuxiaActive()
    {
        return GameTicker.IsGameRuleActive<WuxiaWorldRuleComponent>();
    }
}
