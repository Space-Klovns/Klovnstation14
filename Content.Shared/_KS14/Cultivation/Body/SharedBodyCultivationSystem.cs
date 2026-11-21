using Robust.Shared.Utility;
using Content.Shared.Verbs;
using Content.Shared.Alert;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.Movement.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Cultivation.Body;

/// <summary>
///     The one place a body cultivator's stage, tempering, exhaustion and part levels change, and where the combined
///         effects of their unlocked powers and active arts are applied to damage, speed and stamina.
/// </summary>
public sealed partial class SharedBodyCultivationSystem : EntitySystem
{

    /// <summary>
    ///     Health added by reaching each stage (index 0 is Copper Skin), cumulative.
    /// </summary>
    public static readonly float[] StageHealth = { 10f, 10f, 10f, 10f, 15f, 15f, 15f, 20f, 20f };

    public static readonly BodyGroup[] AllGroups =
    {
        BodyGroup.Head, BodyGroup.Chest, BodyGroup.Arms, BodyGroup.Legs, BodyGroup.Eyes,
        BodyGroup.Ears, BodyGroup.Heart, BodyGroup.Lungs, BodyGroup.Liver, BodyGroup.Stomach,
    };

    private static readonly string[] BruteTypes = { "Blunt", "Slash", "Piercing" };
    private static readonly string[] BurnTypes = { "Heat", "Cold", "Shock", "Caustic" };

    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private AlertsSystem _alertsSystem = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeedModifierSystem = default!;
    [Dependency] private EntityQuery<BodyCultivatorComponent> _bodyCultivatorQuery = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_netManager.IsClient)
            return;

        var currentTime = _gameTiming.CurTime;
        var hardeningEnumerator = EntityQueryEnumerator<BodyHardeningComponent>();
        while (hardeningEnumerator.MoveNext(out var uid, out var hardeningComponent))
        {
            if (hardeningComponent.BloodBoilEnd != TimeSpan.Zero && currentTime >= hardeningComponent.BloodBoilEnd)
            {
                hardeningComponent.BloodBoilEnd = TimeSpan.Zero;
                AddExhaustion(uid, hardeningComponent.BloodBoilCrash);
                Dirty(uid, hardeningComponent);
                _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(uid);
                var ev = new BodyBloodBoilEndedEvent();
                RaiseLocalEvent(uid, ref ev);
            }

            if (currentTime >= hardeningComponent.IronShirtEnd && currentTime >= hardeningComponent.VajraEnd
                && hardeningComponent.BloodBoilEnd == TimeSpan.Zero)
                RemCompDeferred(uid, hardeningComponent);
        }
    }

    #region Rules

    /// <summary>
    ///     Tempering it takes to raise a part from one level below to this level.
    /// </summary>
    public static int PartCost(int level)
    {
        return 2 + level;
    }

    public static int StageCap(BodyCultivatorComponent bodyCultivator)
    {
        return bodyCultivator.Committed ? BodyCultivatorComponent.MaxStage : BodyCultivatorComponent.MortalStageCap;
    }

    /// <summary>
    ///     Parts can be tempered one level past the current stage, and no further than the path allows.
    /// </summary>
    public static int PartCap(BodyCultivatorComponent bodyCultivator)
    {
        return Math.Min(bodyCultivator.Stage + 1, StageCap(bodyCultivator));
    }

    public static int GetLevel(BodyCultivatorComponent bodyCultivator, BodyGroup group)
    {
        return bodyCultivator.Levels.GetValueOrDefault(group);
    }

    /// <summary>
    ///     How much tempering a body can bank: enough to raise every part through the next two levels, so a whole stage
    ///         can be forged from what's held, and never less than 120.
    /// </summary>
    public static float TemperingCapFor(int stage)
    {
        return MathF.Max(120f, AllGroups.Length * (PartCost(stage + 1) + PartCost(stage + 2)));
    }

    public static float StageHealthBonus(int stage)
    {
        var total = 0f;
        for (var i = 0; i < stage && i < StageHealth.Length; i++)
            total += StageHealth[i];

        return total;
    }

    /// <summary>
    ///     Every part tempered past the current stage, and the path still open above it.
    /// </summary>
    public bool ReadyForTribulation(BodyCultivatorComponent bodyCultivator)
    {
        if (bodyCultivator.Stage >= StageCap(bodyCultivator))
            return false;

        foreach (var group in AllGroups)
        {
            if (GetLevel(bodyCultivator, group) <= bodyCultivator.Stage)
                return false;
        }

        return true;
    }

    /// <summary>
    ///     The least tempered part that can still take more, or null if every part is at the cap.
    /// </summary>
    public BodyGroup? WeakestGroup(BodyCultivatorComponent bodyCultivator)
    {
        var cap = PartCap(bodyCultivator);
        BodyGroup? weakest = null;
        var weakestLevel = int.MaxValue;
        foreach (var group in AllGroups)
        {
            var level = GetLevel(bodyCultivator, group);
            if (level >= cap || level >= weakestLevel)
                continue;

            weakest = group;
            weakestLevel = level;
        }

        return weakest;
    }

    public bool CanExert(BodyCultivatorComponent bodyCultivator, float exhaustion)
    {
        return bodyCultivator.Exhaustion + exhaustion <= bodyCultivator.MaxExhaustion;
    }

    #endregion

    #region Changes

    /// <summary>
    ///     Earn tempering from a kind of training, if that kind isn't on cooldown.
    /// </summary>
    /// <returns>Tempering actually gained.</returns>
    public float AddTempering(Entity<BodyCultivatorComponent?> entity, float amount, string? trainingSource = null, TimeSpan? cooldown = null)
    {
        if (!_bodyCultivatorQuery.Resolve(entity, ref entity.Comp, logMissing: false))
            return 0f;

        // A Primordial Chaos Body is the peak: every part is perfected and there's nothing left to temper.
        if (entity.Comp.Stage >= BodyCultivatorComponent.MaxStage && amount > 0f)
            return 0f;

        var currentTime = _gameTiming.CurTime;
        if (trainingSource != null)
        {
            if (entity.Comp.TrainingCooldowns.TryGetValue(trainingSource, out var ready) && currentTime < ready)
                return 0f;

            entity.Comp.TrainingCooldowns[trainingSource] = currentTime + (cooldown ?? TimeSpan.FromSeconds(30));

            // Training goes faster in good company.
            var multiplierEvent = new BodyTemperingMultiplierEvent(1f);
            RaiseLocalEvent(entity, ref multiplierEvent);
            amount *= multiplierEvent.Multiplier;
        }

        var gained = Math.Clamp(amount, 0f, entity.Comp.TemperingCap - entity.Comp.Tempering);
        if (gained <= 0f)
            return 0f;

        entity.Comp.Tempering += gained;
        Dirty(entity);
        UpdateAlerts((entity, entity.Comp));
        return gained;
    }

    /// <summary>
    ///     Pour tempering into a part, but only as much as it can take before the cap. Leftover stays banked.
    /// </summary>
    /// <returns>Levels gained.</returns>
    public int Forge(Entity<BodyCultivatorComponent?> entity, BodyGroup group, float maxAmount)
    {
        if (!_bodyCultivatorQuery.Resolve(entity, ref entity.Comp, logMissing: false))
            return 0;

        var cap = PartCap(entity.Comp);
        var level = GetLevel(entity.Comp, group);
        if (level >= cap)
            return 0;

        var progress = entity.Comp.Progress.GetValueOrDefault(group);
        var room = PartCost(level + 1) - progress;
        for (var next = level + 2; next <= cap; next++)
            room += PartCost(next);

        var poured = (int)MathF.Min(MathF.Min(maxAmount, entity.Comp.Tempering), (float)room);
        if (poured <= 0)
            return 0;

        entity.Comp.Tempering -= (float)poured;
        progress += poured;

        var gained = 0;
        while (level < cap && progress >= PartCost(level + 1))
        {
            progress -= PartCost(level + 1);
            level++;
            gained++;
        }

        entity.Comp.Levels[group] = level;
        entity.Comp.Progress[group] = progress;
        Dirty(entity);
        UpdateAlerts((entity, entity.Comp));

        if (gained > 0)
            Recalculate((entity, entity.Comp));

        return gained;
    }

    /// <summary>
    ///     Set a part's level directly, as remolding a part from scratch does.
    /// </summary>
    public void SetLevel(Entity<BodyCultivatorComponent?> entity, BodyGroup group, int level)
    {
        if (!_bodyCultivatorQuery.Resolve(entity, ref entity.Comp, logMissing: false))
            return;

        entity.Comp.Levels[group] = Math.Clamp(level, 0, BodyCultivatorComponent.MaxStage);
        entity.Comp.Progress[group] = 0;
        Dirty(entity);
        Recalculate((entity, entity.Comp));
    }

    /// <summary>
    ///     Iron Shirt, the Vajra Golden Body and Blood Boil all end at once: cut through, or sealed away.
    /// </summary>
    public bool BreakHardening(EntityUid uid)
    {
        if (!TryComp<BodyHardeningComponent>(uid, out var hardeningComponent))
            return false;

        var now = _gameTiming.CurTime;
        var broke = hardeningComponent.IronShirtEnd > now || hardeningComponent.VajraEnd > now;
        hardeningComponent.IronShirtEnd = now;
        hardeningComponent.VajraEnd = now;
        hardeningComponent.BloodBoilEnd = now;
        Dirty(uid, hardeningComponent);
        return broke;
    }

    public void AddExhaustion(Entity<BodyCultivatorComponent?> entity, float amount)
    {
        if (!_bodyCultivatorQuery.Resolve(entity, ref entity.Comp, logMissing: false))
            return;

        var newExhaustion = Math.Clamp(entity.Comp.Exhaustion + amount, 0f, entity.Comp.MaxExhaustion);
        if (MathHelper.CloseTo(newExhaustion, entity.Comp.Exhaustion))
            return;

        entity.Comp.Exhaustion = newExhaustion;
        Dirty(entity);
        UpdateAlerts((entity, entity.Comp));
    }

    public void Commit(Entity<BodyCultivatorComponent?> entity)
    {
        if (!_bodyCultivatorQuery.Resolve(entity, ref entity.Comp, logMissing: false) || entity.Comp.Committed)
            return;

        entity.Comp.Committed = true;
        Dirty(entity);
        // The Molding Art's first exercises: enough banked to forge every part once straight away, so the path can
        //     be walked from the moment it's chosen.
        AddTempering(entity, StartingTempering);
    }

    /// <summary>
    ///     Tempering banked on committing to the Body Molding Art: one level for every part.
    /// </summary>
    public static readonly float StartingTempering = AllGroups.Length * PartCost(1);

    /// <summary>
    ///     Everything forged into the body, as tempering points: every part's levels and progress, plus what's pending.
    /// </summary>
    public static float TotalInvested(BodyCultivatorComponent bodyCultivator)
    {
        var total = bodyCultivator.Tempering;
        foreach (var (group, level) in bodyCultivator.Levels)
        {
            for (var reached = 1; reached <= level; reached++)
                total += PartCost(reached);

            total += bodyCultivator.Progress.GetValueOrDefault(group);
        }

        return total;
    }

    /// <summary>
    ///     The forged body has left: every part returns to plain flesh, back to the very start of the path.
    /// </summary>
    public void ResetBody(Entity<BodyCultivatorComponent> entity)
    {
        entity.Comp.Stage = 0;
        entity.Comp.Tempering = 0f;
        entity.Comp.Levels.Clear();
        entity.Comp.Progress.Clear();
        Recalculate(entity);
        Dirty(entity);
        UpdateAlerts(entity);
    }

    public void AdvanceStage(Entity<BodyCultivatorComponent?> entity)
    {
        if (!_bodyCultivatorQuery.Resolve(entity, ref entity.Comp, logMissing: false)
            || entity.Comp.Stage >= StageCap(entity.Comp))
            return;

        var oldStage = entity.Comp.Stage;
        entity.Comp.Stage++;
        Dirty(entity);
        Recalculate((entity, entity.Comp));

        var ev = new BodyStageChangedEvent(oldStage, entity.Comp.Stage);
        RaiseLocalEvent(entity, ref ev);
    }

    /// <summary>
    ///     Recombine the stage and every unlocked power into the body's modifiers.
    /// </summary>
    /// <returns>Powers unlocked by this change.</returns>
    public List<BodyTemperingPowerPrototype> Recalculate(Entity<BodyCultivatorComponent> entity)
    {
        var bodyCultivator = entity.Comp;
        var newlyUnlocked = new List<BodyTemperingPowerPrototype>();

        bodyCultivator.DamageMultipliers.Clear();
        bodyCultivator.SpeedMultiplier = 1f;
        bodyCultivator.StaminaMultiplier = 1f;
        bodyCultivator.HungerMultiplier = 1f;
        bodyCultivator.RegenPerSecond = 0f;
        bodyCultivator.HealthBonus = StageHealthBonus(bodyCultivator.Stage);
        bodyCultivator.TemperingCap = TemperingCapFor(bodyCultivator.Stage);
        // Fists grow deadly fast: the arms' tempering, plus the square of the stage. A Bronze Skin punches a little
        //     harder than a mortal; a Primordial Chaos Body hits like a falling girder.
        var stage = (float)bodyCultivator.Stage;
        // The stages before the ninth hold back, so the end of the path stands clearly above them. Powers add their
        //     listed bonus on top, unscaled.
        bodyCultivator.PunchDamage = (1.5f * (float)GetLevel(bodyCultivator, BodyGroup.Arms) + 0.6f * stage * stage)
            * StageStrength(bodyCultivator.Stage);
        bodyCultivator.PunchStaminaDamage = 2f * stage;
        bodyCultivator.PunchKnockdownChance = 0f;
        bodyCultivator.PunchThrowChance = 0f;
        bodyCultivator.PunchStructuralDamage = 0f;
        bodyCultivator.PunchBreaksWalls = false;
        bodyCultivator.EternalHeart = false;

        // Each stage also makes you a little faster.
        bodyCultivator.SpeedMultiplier = 1f + 0.01f * (float)bodyCultivator.Stage;

        // A Primordial Chaos Body can't be shocked, and its fists are a clear step above every stage before.
        if (bodyCultivator.Stage >= BodyCultivatorComponent.MaxStage)
            bodyCultivator.DamageMultipliers["Shock"] = 0f;

        foreach (var power in ProtoMan.EnumeratePrototypes<BodyTemperingPowerPrototype>())
        {
            if (GetLevel(bodyCultivator, power.Group) < power.Level)
                continue;

            if (bodyCultivator.Powers.Add(power.ID))
                newlyUnlocked.Add(power);

            foreach (var (type, multiplier) in power.DamageMultipliers)
            {
                bodyCultivator.DamageMultipliers[type] = bodyCultivator.DamageMultipliers.TryGetValue(type, out var current)
                    ? MathF.Min(current, multiplier)
                    : multiplier;
            }

            bodyCultivator.SpeedMultiplier = MathF.Max(bodyCultivator.SpeedMultiplier, power.SpeedMultiplier);
            bodyCultivator.StaminaMultiplier = MathF.Min(bodyCultivator.StaminaMultiplier, power.StaminaMultiplier);
            bodyCultivator.HungerMultiplier = MathF.Min(bodyCultivator.HungerMultiplier, power.HungerMultiplier);
            bodyCultivator.RegenPerSecond += power.RegenPerSecond;
            bodyCultivator.HealthBonus += power.HealthBonus;
            bodyCultivator.PunchDamage += power.PunchDamage;
            bodyCultivator.PunchStaminaDamage += power.PunchStaminaDamage;
            bodyCultivator.PunchKnockdownChance = MathF.Max(bodyCultivator.PunchKnockdownChance, power.PunchKnockdownChance);
            bodyCultivator.PunchThrowChance = MathF.Max(bodyCultivator.PunchThrowChance, power.PunchThrowChance);
            bodyCultivator.PunchStructuralDamage = MathF.Max(bodyCultivator.PunchStructuralDamage, power.PunchStructuralDamage);
            bodyCultivator.PunchBreaksWalls |= power.PunchBreaksWalls;
            bodyCultivator.EternalHeart |= power.EternalHeart;
        }

        Dirty(entity);
        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(entity);

        var ev = new BodyPowersChangedEvent(newlyUnlocked);
        RaiseLocalEvent(entity, ref ev);
        return newlyUnlocked;
    }

    /// <summary>
    ///     Heaven tests the later stages of the flesh harder. Up to the fourth stage readiness is taken as it is; past it
    ///         every stage takes 4 more, and the last step, into a Primordial Chaos Body, takes 25 in all: certain only with
    ///         every preparation.
    /// </summary>
    public static int TribulationPenalty(int stage)
    {
        return Math.Max(0, stage - 3) * 4 + (stage >= BodyCultivatorComponent.MaxStage - 1 ? 5 : 0);
    }

    /// <summary>
    ///     The chance a Tribulation of Flesh from this stage holds together: certain at 70 effective readiness.
    /// </summary>
    public static float TribulationChance(int readiness, int stage)
    {
        var effective = readiness - TribulationPenalty(stage);
        return effective >= 70 ? 1f : (float)Math.Clamp(effective, 5, 95) / 100f;
    }

    /// <summary>
    ///     From the fifth stage the flesh draws heaven's eye too: lightning falls during the tribulation, more often and
    ///         harder each stage. The last cycle of the final step always strikes.
    /// </summary>
    public static (float Chance, float Damage) TribulationLightning(int stage)
    {
        if (stage < 4)
            return (0f, 0f);

        return (0.2f + 0.06f * (stage - 4), 5f + 1.75f * stage);
    }

    /// <summary>
    ///     How much of their strength a body cultivator's blows and arts carry at a stage. Only the ninth stage, a
    ///         Primordial Chaos Body, hits with all of it; every stage before holds back, so the last step up is a big one.
    /// </summary>
    public static float StageStrength(int stage)
    {
        return stage >= BodyCultivatorComponent.MaxStage ? 1f : 0.55f + 0.03f * stage;
    }

    /// <summary>
    ///     Note that this body cultivator beat a victim down, unless they already did so too recently to learn from it.
    /// </summary>
    /// <returns>Whether it teaches anything this time.</returns>
    public bool TryMarkDefeated(BodyCultivatorComponent bodyCultivator, EntityUid victim, TimeSpan teachesAgain)
    {
        var now = _gameTiming.CurTime;
        if (bodyCultivator.RecentlyDefeated.TryGetValue(victim, out var ready) && now < ready)
            return false;

        bodyCultivator.RecentlyDefeated[victim] = teachesAgain;
        return true;
    }

    public void SetNextTick(BodyCultivatorComponent bodyCultivator, TimeSpan next)
    {
        bodyCultivator.NextTick = next;
    }

    public void SetNextPassiveTempering(BodyCultivatorComponent bodyCultivator, TimeSpan next)
    {
        bodyCultivator.NextPassiveTempering = next;
    }

    public void SetEternalHeartReady(BodyCultivatorComponent bodyCultivator, TimeSpan ready)
    {
        bodyCultivator.EternalHeartReady = ready;
    }

    public void SetBaseThresholds(BodyCultivatorComponent bodyCultivator, float? critical, float? dead)
    {
        bodyCultivator.BaseCriticalThreshold = critical;
        bodyCultivator.BaseDeadThreshold = dead;
    }

    public void SetTribulation(BodyCultivatorComponent bodyCultivator, int cyclesLeft, int readiness)
    {
        bodyCultivator.TribulationCyclesLeft = cyclesLeft;
        bodyCultivator.TribulationReadiness = readiness;
    }

    public void SetEternalHeartRevive(BodyCultivatorComponent bodyCultivator, TimeSpan? reviveAt)
    {
        bodyCultivator.EternalHeartReviveAt = reviveAt;
    }

    public void RecordBaseCooldown(BodyCultivatorComponent bodyCultivator, EntProtoId actionProto, TimeSpan cooldown)
    {
        bodyCultivator.BaseCooldowns[actionProto] = cooldown;
    }

    public TimeSpan? GetBaseCooldown(BodyCultivatorComponent bodyCultivator, EntProtoId actionProto)
    {
        return bodyCultivator.BaseCooldowns.TryGetValue(actionProto, out var cooldown) ? cooldown : null;
    }

    public void SetForgeTarget(BodyCultivatorComponent bodyCultivator, BodyGroup? group)
    {
        bodyCultivator.ForgeTarget = group;
    }

    public EntityUid? GetTechnique(BodyCultivatorComponent bodyCultivator, EntProtoId actionProto)
    {
        return bodyCultivator.Techniques.TryGetValue(actionProto, out var actionUid) ? actionUid : null;
    }

    public void RecordTechnique(BodyCultivatorComponent bodyCultivator, EntProtoId actionProto, EntityUid actionUid)
    {
        bodyCultivator.Techniques[actionProto] = actionUid;
    }

    public bool HasTechnique(BodyCultivatorComponent bodyCultivator, EntProtoId actionProto)
    {
        return bodyCultivator.Techniques.ContainsKey(actionProto);
    }

    public void UpdateAlerts(Entity<BodyCultivatorComponent> entity)
    {
        _alertsSystem.ShowAlert(entity.Owner, entity.Comp.ExhaustionAlert);
        _alertsSystem.ShowAlert(entity.Owner, entity.Comp.TemperingAlert);
    }

    #endregion

    #region Stances and hardening

    public BodyStance? GetStance(EntityUid uid)
    {
        return TryComp<BodyStanceComponent>(uid, out var stanceComponent) ? stanceComponent.Stance : null;
    }

    public void SetStance(EntityUid uid, BodyStance? stance)
    {
        if (stance is not { } newStance)
        {
            RemComp<BodyStanceComponent>(uid);
        }
        else
        {
            var stanceComponent = EnsureComp<BodyStanceComponent>(uid);
            stanceComponent.Stance = newStance;
            Dirty(uid, stanceComponent);
        }

        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(uid);
    }

    public void ApplyIronShirt(EntityUid uid, TimeSpan duration, float bruteMultiplier)
    {
        var hardeningComponent = EnsureComp<BodyHardeningComponent>(uid);
        hardeningComponent.IronShirtEnd = _gameTiming.CurTime + duration;
        hardeningComponent.BruteMultiplier = bruteMultiplier;
        Dirty(uid, hardeningComponent);
    }

    public void ApplyVajra(EntityUid uid, TimeSpan duration)
    {
        var hardeningComponent = EnsureComp<BodyHardeningComponent>(uid);
        hardeningComponent.VajraEnd = _gameTiming.CurTime + duration;
        Dirty(uid, hardeningComponent);
    }

    public void ApplyBloodBoil(EntityUid uid, TimeSpan duration, float crash)
    {
        var hardeningComponent = EnsureComp<BodyHardeningComponent>(uid);
        hardeningComponent.BloodBoilEnd = _gameTiming.CurTime + duration;
        hardeningComponent.BloodBoilCrash = crash;
        Dirty(uid, hardeningComponent);
        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(uid);
    }

    public bool InBloodBoil(EntityUid uid)
    {
        return TryComp<BodyHardeningComponent>(uid, out var hardeningComponent)
            && hardeningComponent.BloodBoilEnd != TimeSpan.Zero
            && _gameTiming.CurTime < hardeningComponent.BloodBoilEnd;
    }

    public bool InVajra(EntityUid uid)
    {
        return TryComp<BodyHardeningComponent>(uid, out var hardeningComponent) && _gameTiming.CurTime < hardeningComponent.VajraEnd;
    }

    #endregion

    #region Effects

    [SubscribeLocalEvent]
    private void OnDamageModify(Entity<BodyCultivatorComponent> entity, ref DamageModifyEvent args)
    {
        var stance = GetStance(entity);
        foreach (var type in new List<ProtoId<DamageTypePrototype>>(args.Damage.DamageDict.Keys))
        {
            var amount = args.Damage.DamageDict[type];
            if (amount <= 0)
                continue;

            var multiplier = entity.Comp.DamageMultipliers.GetValueOrDefault(type.Id, 1f);
            if (stance == BodyStance.Thunderclap && (Array.IndexOf(BruteTypes, type.Id) >= 0 || Array.IndexOf(BurnTypes, type.Id) >= 0))
                multiplier *= 1.1f;
            else if (stance == BodyStance.Mountain && Array.IndexOf(BruteTypes, type.Id) >= 0)
                multiplier *= 0.9f;

            args.Damage.DamageDict[type] = amount * multiplier;
        }
    }

    [SubscribeLocalEvent]
    private void OnHardeningDamageModify(Entity<BodyHardeningComponent> entity, ref DamageModifyEvent args)
    {
        var currentTime = _gameTiming.CurTime;
        var ironShirt = currentTime < entity.Comp.IronShirtEnd;
        var vajra = currentTime < entity.Comp.VajraEnd;
        if (!ironShirt && !vajra)
            return;

        foreach (var type in new List<ProtoId<DamageTypePrototype>>(args.Damage.DamageDict.Keys))
        {
            var amount = args.Damage.DamageDict[type];
            if (amount <= 0)
                continue;

            var brute = Array.IndexOf(BruteTypes, type.Id) >= 0;
            var burn = Array.IndexOf(BurnTypes, type.Id) >= 0;
            var multiplier = 1f;
            if (ironShirt && brute)
                multiplier *= entity.Comp.BruteMultiplier;
            if (vajra && (brute || burn))
                multiplier *= entity.Comp.VajraMultiplier;

            args.Damage.DamageDict[type] = amount * multiplier;
        }
    }

    [SubscribeLocalEvent]
    private void OnRefreshSpeed(Entity<BodyCultivatorComponent> entity, ref RefreshMovementSpeedModifiersEvent args)
    {
        var multiplier = entity.Comp.SpeedMultiplier;
        switch (GetStance(entity))
        {
            case BodyStance.Thunderclap:
                multiplier *= 1.15f;
                break;
            case BodyStance.Mountain:
                multiplier *= 0.8f;
                break;
        }

        // Blood Boil: nothing slows you.
        if (InBloodBoil(entity))
            multiplier = MathF.Max(multiplier, 1.2f);

        args.ModifySpeed(multiplier);
    }

    [SubscribeLocalEvent]
    private void OnStaminaDamage(Entity<BodyCultivatorComponent> entity, ref BeforeStaminaDamageEvent args)
    {
        if (args.Value > 0)
            args.Value *= entity.Comp.StaminaMultiplier;
    }

    [SubscribeLocalEvent]
    private void OnExamined(Entity<BodyCultivatorComponent> entity, ref ExaminedEvent args)
    {
        // Looking yourself over: your stage and banked tempering in one line. The powers wait behind the examine button.
        if (args.Examiner == entity.Owner && entity.Comp.Committed)
        {
            args.PushMarkup(Loc.GetString("body-cultivation-self",
                ("stage", entity.Comp.Stage > 0 ? Loc.GetString($"body-stage-{entity.Comp.Stage}") : Loc.GetString("body-stage-none")),
                ("tempering", (int) entity.Comp.Tempering), ("count", entity.Comp.Powers.Count)));
            return;
        }

        if (entity.Comp.Stage <= 0)
            return;

        args.PushMarkup(Loc.GetString("body-cultivation-examine",
            ("target", entity.Owner),
            ("stage", Loc.GetString($"body-stage-{entity.Comp.Stage}"))));
    }

    /// <summary>
    ///     A button in your own examine box that opens the list of every power your body has unlocked.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnShutdown(Entity<BodyCultivatorComponent> entity, ref ComponentShutdown args)
    {
        _alertsSystem.ClearAlert(entity.Owner, entity.Comp.ExhaustionAlert);
        _alertsSystem.ClearAlert(entity.Owner, entity.Comp.TemperingAlert);
    }

    #endregion
}

/// <summary>
///     Raised on a body cultivator whenever their powers are recombined, with any powers newly unlocked.
/// </summary>
[ByRefEvent]
public readonly record struct BodyPowersChangedEvent(List<BodyTemperingPowerPrototype> NewlyUnlocked);

[ByRefEvent]
public readonly record struct BodyBloodBoilEndedEvent;
