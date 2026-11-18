using Content.Shared.GameTicking.Components;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     What an admin tool (the field commander admin window, the console) changes a running commander through. Every
///         value is checked here, on the server, whatever sent it: non-finite numbers are refused, the rest clamped,
///         prototype ids looked up.
/// </summary>
public sealed partial class KsFieldCommanderRuleSystem
{
    #region Admin editing

    public const float MaxEditedBudget = 10000f;
    public const float MaxEditedCost = 1000f;
    public const float MaxEditedWeight = 1000f;
    public const int MaxEditedCount = 50;
    public static readonly TimeSpan MaxEditedTime = TimeSpan.FromHours(24);

    /// <summary>
    ///     Where a commander is, for an admin tool to show. Names are locale ids, for the admin's own client to read in
    ///         their language.
    /// </summary>
    public KsFieldCommanderAdminStatus GetAdminStatus(Entity<KsFieldCommanderRuleComponent, GameRuleComponent> rule)
    {
        var component = rule.Comp1;
        var curTime = GetCommanderTime(component);
        var elapsed = GetElapsed(component, rule.Comp2, curTime);
        var defenders = SurveyBattlefield(component).Defenders.Count;

        LocId? phaseName = TryGetPhase(component, out var phase) ? (LocId?)phase.Name : null;
        TimeSpan? nextPhaseIn = component.PhaseIndex + 1 < component.Phases.Count
            ? component.Phases[component.PhaseIndex + 1].Start - elapsed
            : null;
        TimeSpan? nextWaveIn = component.NextWaveTime == TimeSpan.MaxValue ? null : component.NextWaveTime - curTime;
        LocId? doctrineName = component.DoctrineIndex >= 0 && component.DoctrineIndex < component.Doctrines.Count
            ? (LocId?)component.Doctrines[component.DoctrineIndex].Name
            : null;

        return new KsFieldCommanderAdminStatus(component.PhaseIndex,
            phaseName,
            elapsed,
            nextPhaseIn,
            nextWaveIn,
            CountLivingUnits(rule.Owner, null),
            GetMaxAlive(component, defenders),
            defenders,
            component.CarriedBudget,
            component.AdaptiveMultiplier,
            component.WavesSent,
            component.QueuedWaves.Count,
            IsPaused(component),
            doctrineName);
    }

    /// <summary>
    ///     Changes a phase's start, wave interval or threat budget; null leaves one as it is. A start is kept between
    ///         its neighbours' so that the timeline stays in order. False, with nothing changed, for an unknown phase, a
    ///         non-finite budget or a start out of order.
    /// </summary>
    public bool TryEditPhase(Entity<KsFieldCommanderRuleComponent> rule, int phaseIndex, TimeSpan? start, TimeSpan? waveInterval, float? budget)
    {
        var phases = rule.Comp.Phases;
        if (phaseIndex < 0 || phaseIndex >= phases.Count ||
            budget is { } newBudget && !float.IsFinite(newBudget))
            return false;

        var phase = phases[phaseIndex];

        if (start is { } newStart)
        {
            newStart = ClampTime(newStart);
            if (phaseIndex > 0 && newStart < phases[phaseIndex - 1].Start ||
                phaseIndex + 1 < phases.Count && newStart > phases[phaseIndex + 1].Start)
                return false;

            phase.Start = newStart;
        }

        if (waveInterval is { } newInterval)
        {
            phase.WaveInterval = ClampTime(newInterval);

            // Takes effect from the current phase's next wave; a phase that sent none starts now.
            if (phaseIndex == rule.Comp.PhaseIndex && rule.Comp.NextWaveTime == TimeSpan.MaxValue && phase.WaveInterval > TimeSpan.Zero)
                SetNextWave(rule.Comp, GetCommanderTime(rule.Comp) + phase.WaveInterval);
        }

        if (budget is { } setBudget)
            phase.Budget = Math.Clamp(setBudget, 0f, MaxEditedBudget);

        return true;
    }

    /// <summary>
    ///     Changes an asset; any field of <paramref name="edit"/> left null stays as it is. False, with nothing
    ///         changed, for an unknown asset, an unknown or abstract prototype, or a non-finite number.
    /// </summary>
    public bool TryEditAsset(Entity<KsFieldCommanderRuleComponent> rule, int assetIndex, KsFieldCommanderAssetEdit edit)
    {
        if (assetIndex < 0 || assetIndex >= rule.Comp.Assets.Count || !IsValidEdit(edit))
            return false;

        ApplyEdit(rule.Comp, rule.Comp.Assets[assetIndex], edit);
        return true;
    }

    /// <summary>
    ///     Adds an asset, starting from the defaults and changed by <paramref name="edit"/>, which has to name what it
    ///         sends. Returns its index, or -1, with nothing added, for an edit that would be refused.
    /// </summary>
    public int TryAddAsset(Entity<KsFieldCommanderRuleComponent> rule, KsFieldCommanderAssetEdit edit)
    {
        if (edit.Entity == null && edit.Humanoid == null || !IsValidEdit(edit))
            return -1;

        var asset = new KsFieldCommanderAsset();
        ApplyEdit(rule.Comp, asset, edit);
        rule.Comp.Assets.Add(asset);
        return rule.Comp.Assets.Count - 1;
    }

    /// <summary>
    ///     Disables an asset, or brings one back. It is never removed: units in the field point at it by index.
    /// </summary>
    public bool TrySetAssetDisabled(Entity<KsFieldCommanderRuleComponent> rule, int assetIndex, bool disabled)
    {
        if (assetIndex < 0 || assetIndex >= rule.Comp.Assets.Count)
            return false;

        rule.Comp.Assets[assetIndex].Disabled = disabled;
        return true;
    }

    private bool IsValidEdit(KsFieldCommanderAssetEdit edit)
    {
        if (edit.Cost is { } cost && !float.IsFinite(cost) ||
            edit.Weight is { } weight && !float.IsFinite(weight) ||
            edit.Delivery is { } delivery && !Enum.IsDefined(delivery))
            return false;

        if (edit.Humanoid is { } humanoid && !ProtoMan.HasIndex(humanoid))
            return false;

        return edit.Entity is not { } entity ||
               ProtoMan.TryIndex(entity, out var entityPrototype) && !entityPrototype.Abstract;
    }

    private static void ApplyEdit(KsFieldCommanderRuleComponent component, KsFieldCommanderAsset asset, KsFieldCommanderAssetEdit edit)
    {
        // One or the other: a humanoid takes precedence over an entity, so setting either clears both first.
        if (edit.Humanoid != null || edit.Entity != null)
        {
            asset.Humanoid = edit.Humanoid;
            asset.Entity = edit.Entity;
        }

        if (edit.Count is { } count)
            asset.Count = Math.Clamp(count, 1, MaxEditedCount);

        if (edit.Cost is { } cost)
            asset.Cost = Math.Clamp(cost, 0f, MaxEditedCost);

        if (edit.Weight is { } weight)
            asset.Weight = Math.Clamp(weight, 0f, MaxEditedWeight);

        if (edit.MinPhase is { } minPhase)
            asset.MinPhase = Math.Clamp(minPhase, 0, Math.Max(0, component.Phases.Count - 1));

        if (edit.MaxAlive is { } maxAlive)
            asset.MaxAlive = Math.Clamp(maxAlive, 0, component.HardMaxAlive);

        if (edit.Delivery is { } delivery)
            asset.Delivery = delivery;
    }

    private static TimeSpan ClampTime(TimeSpan time)
    {
        return time < TimeSpan.Zero ? TimeSpan.Zero : time > MaxEditedTime ? MaxEditedTime : time;
    }

    #endregion
}

/// <summary>
///     See <see cref="KsFieldCommanderRuleSystem.GetAdminStatus"/>.
/// </summary>
/// <param name="PhaseName">Locale id of the current phase's name, or null before the first.</param>
/// <param name="NextPhaseIn">Null when this is the last phase.</param>
/// <param name="NextWaveIn">Null when no wave is scheduled.</param>
/// <param name="DoctrineName">Locale id of the last wave's doctrine, if it had one.</param>
public readonly record struct KsFieldCommanderAdminStatus(
    int PhaseIndex,
    LocId? PhaseName,
    TimeSpan Elapsed,
    TimeSpan? NextPhaseIn,
    TimeSpan? NextWaveIn,
    int Alive,
    int Cap,
    int Defenders,
    float CarriedBudget,
    float AdaptiveMultiplier,
    int WavesSent,
    int QueuedWaves,
    bool Paused,
    LocId? DoctrineName);

/// <summary>
///     A change to a <see cref="KsFieldCommanderAsset"/>, for <see cref="KsFieldCommanderRuleSystem.TryEditAsset"/>
///         and <see cref="KsFieldCommanderRuleSystem.TryAddAsset"/>: every field left null stays as it is.
/// </summary>
public readonly record struct KsFieldCommanderAssetEdit(
    EntProtoId? Entity = null,
    ProtoId<RandomHumanoidSettingsPrototype>? Humanoid = null,
    int? Count = null,
    float? Cost = null,
    float? Weight = null,
    int? MinPhase = null,
    int? MaxAlive = null,
    KsFieldCommanderDelivery? Delivery = null);
