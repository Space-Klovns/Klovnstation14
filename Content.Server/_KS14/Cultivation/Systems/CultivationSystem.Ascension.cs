using Content.Server.Chat.Systems;
using Content.Server.Ghost;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Mind;
using Content.Shared.Popups;
using Robust.Shared.Random;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     The end of the path. Past the peak of Nascent Soul, with enough insight, a cultivator calls down the final
///         tribulation: a minute of lightning while their last heart demon claws at them, with the whole station
///         watching. Succeed and they shatter the void and leave this world for good; fail and heaven smashes them
///         down, scattering half their foundation.
/// </summary>
public sealed partial class CultivationSystem
{
    private const int AscensionCycles = 20;
    private const float AscensionLightningChance = 0.4f;
    private const float AscensionLightningDamage = 15f;

    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private GhostSystem _ghostSystem = default!;
    [Dependency] private SharedMindSystem _mindSystem = default!;

    public bool StartAscension(Entity<CultivatorComponent> entity)
    {
        var readiness = BreakthroughReadiness(entity);
        var doAfterArgs = new DoAfterArgs(EntityManager, entity, entity.Comp.BreakthroughCycle, new CultivationAscensionDoAfterEvent(), entity)
        {
            BreakOnMove = true,
            NeedHand = false,
        };

        if (!_doAfterSystem.TryStartDoAfter(doAfterArgs))
            return false;

        _cultivationSystem.SetBreakthrough(entity.Comp, AscensionCycles, readiness);
        _heartDemonSystem.Summon(entity, lifetime: TimeSpan.FromSeconds(AscensionCycles * entity.Comp.BreakthroughCycle.TotalSeconds));
        _noticeSystem.Proclaim(null, Loc.GetString("cultivation-ascension-begins", ("user", entity.Owner)),
            Loc.GetString("cultivation-heavenly-omen"), colorOverride: Color.Gold);
        var effective = SharedCultivationSystem.EffectiveReadiness(readiness, entity.Comp.Realm);
        _noticeSystem.Notify(entity, Loc.GetString(effective >= 70 ? "cultivation-readiness-stable" : "cultivation-readiness-risky",
            ("readiness", effective), ("chance", (int)(SharedCultivationSystem.BreakthroughChance(readiness, entity.Comp.Realm) * 100))),
            type: PopupType.Medium);
        _noticeSystem.Notify(entity, Loc.GetString("cultivation-storm-warning",
            ("chance", (int)(AscensionLightningChance * 100)), ("damage", (int)AscensionLightningDamage)), type: PopupType.Medium);
        return true;
    }

    [SubscribeLocalEvent]
    private void OnAscensionDoAfter(Entity<CultivatorComponent> entity, ref CultivationAscensionDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (args.Cancelled)
        {
            _cultivationSystem.SetBreakthrough(entity.Comp, 0, 0);
            FailAscension(entity);
            return;
        }

        CallLightning(entity.Owner, AscensionLightningChance, AscensionLightningDamage);

        var cyclesLeft = entity.Comp.BreakthroughCyclesLeft - 1;
        _cultivationSystem.SetBreakthrough(entity.Comp, cyclesLeft, entity.Comp.BreakthroughReadiness);
        if (cyclesLeft > 0)
        {
            args.Repeat = true;
            return;
        }

        var readiness = entity.Comp.BreakthroughReadiness;
        _cultivationSystem.SetBreakthrough(entity.Comp, 0, 0);
        if (_robustRandom.Prob(SharedCultivationSystem.BreakthroughChance(readiness, entity.Comp.Realm)))
            Ascend(entity);
        else
            FailAscension(entity);
    }

    /// <summary>
    ///     Shatter the void: the cultivator leaves this world for the Immortal Realm, and the round.
    /// </summary>
    public void Ascend(Entity<CultivatorComponent> entity)
    {
        _visualsSystem.Breakthrough(entity);
        _noticeSystem.Proclaim(null, Loc.GetString("cultivation-ascended", ("user", entity.Owner)),
            Loc.GetString("cultivation-heavenly-omen"), colorOverride: Color.Gold);
        _audioSystem.PlayPvs(BreakthroughSound, entity);

        if (_mindSystem.TryGetMind(entity, out var mindId, out var mindComponent))
            _ghostSystem.OnGhostAttempt(mindId, canReturnGlobal: false, forced: true, mind: mindComponent);

        QueueDel(entity);
    }

    private void FailAscension(Entity<CultivatorComponent> entity)
    {
        _cultivationSystem.ScaleInsight(entity.AsNullable(), 0.5f);
        _cultivationSystem.AddInternalInjury(entity.AsNullable(), stacks: entity.Comp.MaxInternalInjury);
        _damageableSystem.TryChangeDamage(entity.Owner, Damage("Shock", 40f), ignoreResistances: true, interruptsDoAfters: false);
        _audioSystem.PlayPvs(DeviationSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-ascension-failed", ("user", entity.Owner)), type: PopupType.LargeCaution);
    }
}
