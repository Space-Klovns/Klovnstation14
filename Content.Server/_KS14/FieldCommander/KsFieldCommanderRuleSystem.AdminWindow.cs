using System.Diagnostics.CodeAnalysis;
using Content.Shared.GameTicking.Components;

namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     What the field commander admin window needs on top of the admin editing API.
/// </summary>
public sealed partial class KsFieldCommanderRuleSystem
{
    #region Admin window

    public const int MaxEditedGuaranteedEveryWaves = 1000;
    public const int MaxEditedTags = 16;
    public const int MaxEditedTagLength = 32;

    /// <summary>
    ///     The field commander on this entity, if it is one and still running.
    /// </summary>
    public bool TryGetActiveRule(EntityUid ruleUid, [NotNullWhen(true)] out Entity<KsFieldCommanderRuleComponent, GameRuleComponent>? rule)
    {
        rule = null;
        var query = QueryActiveRules();
        while (query.MoveNext(out var activeUid, out _, out var ruleComponent, out var gameRuleComponent))
        {
            if (activeUid != ruleUid)
                continue;

            rule = (activeUid, ruleComponent, gameRuleComponent);
            return true;
        }

        return false;
    }

    /// <summary>
    ///     How many of the commander's units are alive, by the index of the asset they were bought as.
    /// </summary>
    public Dictionary<int, int> CountLivingUnitsByAsset(EntityUid ruleUid)
    {
        var aliveByAsset = new Dictionary<int, int>();
        CountLivingUnits(ruleUid, aliveByAsset);
        return aliveByAsset;
    }

    /// <summary>
    ///     <see cref="TryEditAsset(Entity{KsFieldCommanderRuleComponent}, int, KsFieldCommanderAssetEdit)"/>, together
    ///         with when the asset comes whatever the budget buys and the tags doctrines weigh it by. Both are checked
    ///         before either is applied: false, with nothing changed, for an edit either would refuse.
    /// </summary>
    public bool TryEditAsset(Entity<KsFieldCommanderRuleComponent> rule, int assetIndex, KsFieldCommanderAssetEdit edit, KsFieldCommanderAssetScheduleEdit schedule)
    {
        if (assetIndex < 0 || assetIndex >= rule.Comp.Assets.Count || !IsValidEdit(edit) || !IsValidSchedule(schedule))
            return false;

        var asset = rule.Comp.Assets[assetIndex];
        ApplyEdit(rule.Comp, asset, edit);
        ApplySchedule(asset, schedule);
        return true;
    }

    /// <summary>
    ///     <see cref="TryAddAsset(Entity{KsFieldCommanderRuleComponent}, KsFieldCommanderAssetEdit)"/>, with a
    ///         schedule as in <see cref="TryEditAsset(Entity{KsFieldCommanderRuleComponent}, int, KsFieldCommanderAssetEdit, KsFieldCommanderAssetScheduleEdit)"/>.
    /// </summary>
    public int TryAddAsset(Entity<KsFieldCommanderRuleComponent> rule, KsFieldCommanderAssetEdit edit, KsFieldCommanderAssetScheduleEdit schedule)
    {
        if (!IsValidSchedule(schedule))
            return -1;

        var index = TryAddAsset(rule, edit);
        if (index >= 0)
            ApplySchedule(rule.Comp.Assets[index], schedule);

        return index;
    }

    /// <summary>
    ///     Refuses a tag that is blank, too long or holds a comma (the window lists them comma-separated), and more
    ///         tags than <see cref="MaxEditedTags"/>.
    /// </summary>
    private static bool IsValidSchedule(KsFieldCommanderAssetScheduleEdit schedule)
    {
        if (schedule.Tags is not { } tags)
            return true;

        if (tags.Count > MaxEditedTags)
            return false;

        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag) || tag.Trim().Length > MaxEditedTagLength || tag.Contains(','))
                return false;
        }

        return true;
    }

    private static void ApplySchedule(KsFieldCommanderAsset asset, KsFieldCommanderAssetScheduleEdit schedule)
    {
        if (schedule.GuaranteedEveryWaves is { } every)
            asset.GuaranteedEveryWaves = Math.Clamp(every, 0, MaxEditedGuaranteedEveryWaves);

        if (schedule.GuaranteedOnPhaseEnter is { } onPhaseEnter)
            asset.GuaranteedOnPhaseEnter = onPhaseEnter;

        if (schedule.Tags is { } tags)
        {
            asset.Tags.Clear();
            foreach (var tag in tags)
                asset.Tags.Add(tag.Trim());
        }
    }

    #endregion
}

/// <summary>
///     A change to when an asset comes whatever the budget buys, and to its tags; every field left null stays as it
///         is. Tags, when given, replace the asset's.
/// </summary>
public readonly record struct KsFieldCommanderAssetScheduleEdit(
    int? GuaranteedEveryWaves = null,
    bool? GuaranteedOnPhaseEnter = null,
    IReadOnlyList<string>? Tags = null);
