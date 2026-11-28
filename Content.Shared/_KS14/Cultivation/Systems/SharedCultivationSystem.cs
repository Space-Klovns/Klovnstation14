using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Prototypes;
using Content.Shared.Alert;
using Content.Shared.Examine;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Cultivation.Systems;

/// <summary>
///     The one place a cultivator's qi, realm, insight, laws and injuries change. Every other system goes through
///         this API, so the alerts and networked state stay in step.
/// </summary>
public sealed partial class SharedCultivationSystem : EntitySystem
{

    [Dependency] private AlertsSystem _alertsSystem = default!;
    [Dependency] private Robust.Shared.Timing.IGameTiming _gameTiming = default!;
    [Dependency] private EntityQuery<CultivatorComponent> _cultivatorQuery = default!;

    #region Pure rules

    /// <summary>
    ///     Fraction of normal qi recovery left at a given internal injury.
    /// </summary>
    public static float RegenMultiplier(float penaltyPerStack, int injury)
    {
        return MathF.Max(0f, 1f - penaltyPerStack * injury);
    }

    /// <summary>
    ///     Chance a breakthrough succeeds at a given readiness: certain from 70, otherwise between 5% and 95%.
    /// </summary>
    public static float BreakthroughChance(int readiness, CultivationRealm realm = CultivationRealm.QiCondensation)
    {
        var effective = EffectiveReadiness(readiness, realm);
        return effective >= 70 ? 1f : (float)Math.Clamp(effective, 5, 95) / 100f;
    }

    /// <summary>
    ///     Heaven tests the higher realms harder: every realm already climbed takes 20 from readiness. Breaking out of
    ///         Qi Condensation is certain at 70, out of Foundation at 90, out of Golden Core at 110.
    /// </summary>
    public static int EffectiveReadiness(int readiness, CultivationRealm realm)
    {
        return readiness - 20 * (int)realm;
    }

    /// <summary>
    ///     How many laws someone at this realm can hold: one, and a second Complementary Law from Foundation.
    /// </summary>
    public static int LawSlots(CultivationRealm realm)
    {
        return realm >= CultivationRealm.FoundationEstablishment ? 2 : 1;
    }

    #endregion

    #region Queries

    public float GetMaxQi(CultivatorComponent cultivator)
    {
        var index = Math.Clamp((int)cultivator.Realm, 0, cultivator.MaxQiByRealm.Count - 1);
        return cultivator.MaxQiByRealm[index];
    }

    /// <summary>
    ///     Insight needed to break out of the current realm, or null at the peak.
    /// </summary>
    public float? GetInsightRequired(CultivatorComponent cultivator)
    {
        var index = (int)cultivator.Realm;
        return index < cultivator.InsightByRealm.Count ? cultivator.InsightByRealm[index] : null;
    }

    public bool ReadyToBreakThrough(CultivatorComponent cultivator)
    {
        return GetInsightRequired(cultivator) is { } required && cultivator.Insight >= required;
    }

    public ProtoId<CultivationLawPrototype>? GetRootLaw(CultivatorComponent cultivator)
    {
        return cultivator.Laws.Count > 0 ? cultivator.Laws[0] : (ProtoId<CultivationLawPrototype>?)null;
    }

    /// <summary>
    ///     Is this element the cultivator's Root Law? Root Laws twist one of their techniques.
    /// </summary>
    public bool HasRoot(EntityUid uid, CultivationElement element)
    {
        if (!_cultivatorQuery.TryComp(uid, out var cultivator) || GetRootLaw(cultivator) is not { } root)
            return false;

        return ProtoMan.TryIndex(root, out var law) && law.Element == element;
    }

    /// <summary>
    ///     The action of a technique the cultivator knows, if they know it.
    /// </summary>
    public EntityUid? TechniqueAction(CultivatorComponent cultivator, EntProtoId actionProto)
    {
        return cultivator.TechniqueActions.TryGetValue(actionProto, out var actionUid) ? actionUid : null;
    }

    public bool HasTechnique(CultivatorComponent cultivator, EntProtoId actionProto)
    {
        return cultivator.TechniqueActions.ContainsKey(actionProto);
    }

    public bool KnowsLaw(CultivatorComponent cultivator, ProtoId<CultivationLawPrototype> law)
    {
        return cultivator.Laws.Contains(law);
    }

    /// <summary>
    ///     Friends, for techniques that help them or spare them: yourself, and anyone something else vouches for.
    /// </summary>
    public bool IsAlly(EntityUid userUid, EntityUid otherUid)
    {
        if (userUid == otherUid)
            return true;

        var ev = new CultivationAllyCheckEvent(otherUid);
        RaiseLocalEvent(userUid, ref ev);
        return ev.Ally;
    }

    #endregion

    #region Changes

    /// <summary>
    ///     Spend qi if there is enough. Doesn't roll for backlash; see the server's technique helper for that.
    /// </summary>
    public bool TrySpendQi(Entity<CultivatorComponent?> entity, float amount)
    {
        if (!_cultivatorQuery.Resolve(entity, ref entity.Comp, logMissing: false) || entity.Comp.Qi < amount)
            return false;

        entity.Comp.Qi -= amount;
        Dirty(entity);
        UpdateAlerts((entity, entity.Comp));
        return true;
    }

    public void AddQi(Entity<CultivatorComponent?> entity, float amount)
    {
        if (!_cultivatorQuery.Resolve(entity, ref entity.Comp, logMissing: false))
            return;

        var newQi = Math.Clamp(entity.Comp.Qi + amount, 0f, GetMaxQi(entity.Comp));
        if (MathHelper.CloseTo(newQi, entity.Comp.Qi))
            return;

        entity.Comp.Qi = newQi;
        Dirty(entity);
        UpdateAlerts((entity, entity.Comp));
    }

    public void AddInsight(Entity<CultivatorComponent?> entity, float amount)
    {
        if (!_cultivatorQuery.Resolve(entity, ref entity.Comp, logMissing: false))
            return;

        // Insight stops piling up once you're ready to break through; the rest is wasted until you do.
        var cap = GetInsightRequired(entity.Comp) ?? 0f;
        entity.Comp.Insight = Math.Clamp(entity.Comp.Insight + amount, 0f, cap);
        Dirty(entity);
    }

    /// <summary>
    ///     Move to a new realm, refilling qi to the new capacity.
    /// </summary>
    public void SetRealm(Entity<CultivatorComponent?> entity, CultivationRealm realm)
    {
        if (!_cultivatorQuery.Resolve(entity, ref entity.Comp, logMissing: false) || entity.Comp.Realm == realm)
            return;

        var oldRealm = entity.Comp.Realm;
        entity.Comp.Realm = realm;
        if (realm > oldRealm)
            entity.Comp.LastBreakthroughAt = _gameTiming.CurTime;
        entity.Comp.Insight = 0f;
        entity.Comp.Qi = GetMaxQi(entity.Comp);
        Dirty(entity);
        UpdateAlerts((entity, entity.Comp));

        var ev = new CultivationRealmChangedEvent(oldRealm, realm);
        RaiseLocalEvent(entity, ref ev);
    }

    /// <summary>
    ///     Lose some insight, as after a failed breakthrough.
    /// </summary>
    public void ScaleInsight(Entity<CultivatorComponent?> entity, float multiplier)
    {
        if (!_cultivatorQuery.Resolve(entity, ref entity.Comp, logMissing: false))
            return;

        entity.Comp.Insight *= multiplier;
        Dirty(entity);
    }

    /// <returns>True if a new stack was added.</returns>
    public bool AddInternalInjury(Entity<CultivatorComponent?> entity, int stacks = 1)
    {
        if (!_cultivatorQuery.Resolve(entity, ref entity.Comp, logMissing: false))
            return false;

        var newInjury = Math.Clamp(entity.Comp.InternalInjury + stacks, 0, entity.Comp.MaxInternalInjury);
        if (newInjury == entity.Comp.InternalInjury)
            return false;

        entity.Comp.InternalInjury = newInjury;
        Dirty(entity);
        UpdateAlerts((entity, entity.Comp));
        return true;
    }

    /// <returns>True if anything healed.</returns>
    public bool HealInternalInjury(Entity<CultivatorComponent?> entity, int stacks = 1)
    {
        if (!_cultivatorQuery.Resolve(entity, ref entity.Comp, logMissing: false) || entity.Comp.InternalInjury <= 0)
            return false;

        entity.Comp.InternalInjury = Math.Max(0, entity.Comp.InternalInjury - stacks);
        Dirty(entity);
        UpdateAlerts((entity, entity.Comp));
        return true;
    }

    /// <summary>
    ///     Count a finished meditation towards healing internal injury.
    /// </summary>
    /// <returns>True if this meditation healed a stack.</returns>
    public bool CountMeditation(Entity<CultivatorComponent?> entity)
    {
        if (!_cultivatorQuery.Resolve(entity, ref entity.Comp, logMissing: false))
            return false;

        if (entity.Comp.InternalInjury <= 0)
        {
            entity.Comp.MeditationsSinceHeal = 0;
            return false;
        }

        entity.Comp.MeditationsSinceHeal++;
        if (entity.Comp.MeditationsSinceHeal < entity.Comp.MeditationsPerInjuryHeal)
            return false;

        entity.Comp.MeditationsSinceHeal = 0;
        return HealInternalInjury(entity);
    }

    /// <summary>
    ///     Learn a law if there's room for it. The first one learned becomes the Root Law.
    /// </summary>
    public bool TryLearnLaw(Entity<CultivatorComponent?> entity, ProtoId<CultivationLawPrototype> law)
    {
        if (!_cultivatorQuery.Resolve(entity, ref entity.Comp, logMissing: false))
            return false;

        if (entity.Comp.Laws.Contains(law) || entity.Comp.Laws.Count >= LawSlots(entity.Comp.Realm))
            return false;

        entity.Comp.Laws.Add(law);
        Dirty(entity);
        return true;
    }

    public void SetBreakthrough(CultivatorComponent cultivator, int cyclesLeft, int readiness)
    {
        cultivator.BreakthroughCyclesLeft = cyclesLeft;
        cultivator.BreakthroughReadiness = readiness;
    }

    public void SetDaoHeartTempered(Entity<CultivatorComponent?> entity, bool tempered)
    {
        if (!_cultivatorQuery.Resolve(entity, ref entity.Comp, logMissing: false))
            return;

        entity.Comp.DaoHeartTempered = tempered;
        Dirty(entity);
    }

    public void SetForcedCirculation(Entity<CultivatorComponent?> entity, bool forced)
    {
        if (!_cultivatorQuery.Resolve(entity, ref entity.Comp, logMissing: false))
            return;

        entity.Comp.ForcedCirculation = forced;
        Dirty(entity);
    }

    /// <summary>
    ///     Let go of a law. The Root Law can never be released.
    /// </summary>
    public bool TryForgetLaw(Entity<CultivatorComponent?> entity, ProtoId<CultivationLawPrototype> law)
    {
        if (!_cultivatorQuery.Resolve(entity, ref entity.Comp, logMissing: false)
            || entity.Comp.Laws.Count < 2 || entity.Comp.Laws[0] == law || !entity.Comp.Laws.Remove(law))
            return false;

        Dirty(entity);
        return true;
    }

    /// <returns>The action entity the technique was granted as, if it was.</returns>
    public EntityUid? ForgetTechnique(CultivatorComponent cultivator, EntProtoId actionProto)
    {
        return cultivator.TechniqueActions.Remove(actionProto, out var actionUid) ? actionUid : null;
    }

    public void SetNextRegen(CultivatorComponent cultivator, TimeSpan next)
    {
        cultivator.NextRegen = next;
    }

    public void SetLastTechnique(CultivatorComponent cultivator, TimeSpan time)
    {
        cultivator.LastTechniqueAt = time;
    }

    public void SetReadyGlow(CultivatorComponent cultivator, bool ready)
    {
        cultivator.ReadyGlow = ready;
    }

    public void SetMeditateAction(CultivatorComponent cultivator, EntityUid? meditate)
    {
        cultivator.MeditateActionEntity = meditate;
    }

    public void RecordTechnique(CultivatorComponent cultivator, EntProtoId actionProto, EntityUid actionUid)
    {
        cultivator.TechniqueActions[actionProto] = actionUid;
    }

    public void UpdateAlerts(Entity<CultivatorComponent> entity)
    {
        _alertsSystem.ShowAlert(entity.Owner, entity.Comp.QiAlert);

        if (entity.Comp.InternalInjury > 0)
            _alertsSystem.ShowAlert(entity.Owner, entity.Comp.InjuryAlert, severity: (short)entity.Comp.InternalInjury);
        else
            _alertsSystem.ClearAlert(entity.Owner, entity.Comp.InjuryAlert);
    }

    #endregion

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<CultivatorComponent> entity, ref ComponentShutdown args)
    {
        _alertsSystem.ClearAlert(entity.Owner, entity.Comp.QiAlert);
        _alertsSystem.ClearAlert(entity.Owner, entity.Comp.InjuryAlert);
    }

    [SubscribeLocalEvent]
    private void OnExamined(Entity<CultivatorComponent> entity, ref ExaminedEvent args)
    {
        // Looking yourself over: realm, qi, and how far you are from breaking through.
        if (args.Examiner == entity.Owner)
        {
            var required = GetInsightRequired(entity.Comp);
            args.PushMarkup(Loc.GetString(required == null ? "cultivation-self-peak" : "cultivation-self",
                ("realm", Loc.GetString($"cultivation-realm-{entity.Comp.Realm}")),
                ("qi", (int) entity.Comp.Qi), ("maxQi", (int) GetMaxQi(entity.Comp)),
                ("insight", (int) entity.Comp.Insight), ("required", (int) (required ?? 0f))));
            return;
        }

        // Anyone can feel the pressure of a Golden Core; below that, or concealed, a cultivator passes for a mortal.
        if (entity.Comp.Realm < CultivationRealm.GoldenCore || HasComp<CultivationConcealedComponent>(entity))
            return;

        args.PushMarkup(Loc.GetString("cultivation-examine-aura",
            ("target", entity.Owner),
            ("realm", Loc.GetString($"cultivation-realm-{entity.Comp.Realm}"))));
    }
}
