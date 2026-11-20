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
    public const float MaxEditedStandOff = 100f;
    public const int MaxEditedKillPoints = 100000;

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
    ///         before either is applied, and so are the <paramref name="details"/>: false, with nothing changed, for an
    ///         edit any of them would refuse.
    /// </summary>
    public bool TryEditAsset(Entity<KsFieldCommanderRuleComponent> rule,
        int assetIndex,
        KsFieldCommanderAssetEdit edit,
        KsFieldCommanderAssetScheduleEdit schedule,
        KsFieldCommanderAssetDetailsEdit details = default)
    {
        if (assetIndex < 0 || assetIndex >= rule.Comp.Assets.Count || !IsValidEdit(edit) || !IsValidSchedule(schedule) || !IsValidDetails(details))
            return false;

        var asset = rule.Comp.Assets[assetIndex];
        ApplyEdit(rule.Comp, asset, edit);
        ApplySchedule(asset, schedule);
        ApplyDetails(asset, details);
        return true;
    }

    /// <summary>
    ///     <see cref="TryAddAsset(Entity{KsFieldCommanderRuleComponent}, KsFieldCommanderAssetEdit)"/>, with a
    ///         schedule as in <see cref="TryEditAsset(Entity{KsFieldCommanderRuleComponent}, int, KsFieldCommanderAssetEdit, KsFieldCommanderAssetScheduleEdit)"/>.
    /// </summary>
    public int TryAddAsset(Entity<KsFieldCommanderRuleComponent> rule,
        KsFieldCommanderAssetEdit edit,
        KsFieldCommanderAssetScheduleEdit schedule,
        KsFieldCommanderAssetDetailsEdit details = default)
    {
        if (!IsValidSchedule(schedule) || !IsValidDetails(details))
            return -1;

        var index = TryAddAsset(rule, edit);
        if (index >= 0)
        {
            ApplySchedule(rule.Comp.Assets[index], schedule);
            ApplyDetails(rule.Comp.Assets[index], details);
        }

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

    /// <summary>
    ///     Refuses a non-finite stand-off, and a locale id that has no string: a name, announcement or intel line that
    ///         would show as its raw id.
    /// </summary>
    private bool IsValidDetails(KsFieldCommanderAssetDetailsEdit details)
    {
        if (details.StandOff is { } standOff && !float.IsFinite(standOff))
            return false;

        if (details.HumanoidName is { } humanoidName && (string.IsNullOrWhiteSpace(humanoidName) || !Loc.HasString(humanoidName.Trim())))
            return false;

        return IsValidOptionalLocId(details.Announcement) && IsValidOptionalLocId(details.Intel);
    }

    /// <summary>
    ///     Null leaves it, blank clears it, anything else has to be a locale id with a string.
    /// </summary>
    private bool IsValidOptionalLocId(string? locId)
    {
        return locId == null || string.IsNullOrWhiteSpace(locId) || Loc.HasString(locId.Trim());
    }

    private static void ApplyDetails(KsFieldCommanderAsset asset, KsFieldCommanderAssetDetailsEdit details)
    {
        if (details.StandOff is { } standOff)
            asset.StandOff = Math.Clamp(standOff, 0f, MaxEditedStandOff);

        // Below zero is none of its own: the kill is worth the cost's share again.
        if (details.KillPoints is { } killPoints)
            asset.KillPoints = killPoints < 0 ? null : Math.Min(killPoints, MaxEditedKillPoints);

        if (details.GiveOrders is { } giveOrders)
            asset.GiveOrders = giveOrders;

        if (details.HumanoidName is { } humanoidName)
            asset.HumanoidName = humanoidName.Trim();

        if (details.Announcement is { } announcement)
            asset.Announcement = string.IsNullOrWhiteSpace(announcement) ? null : announcement.Trim();

        if (details.Intel is { } intel)
            asset.Intel = string.IsNullOrWhiteSpace(intel) ? null : intel.Trim();
    }

    #endregion
}

/// <summary>
///     The rest of an asset the admin window edits; every field left null stays as it is.
/// </summary>
/// <param name="KillPoints">Below zero clears it, for the kill to be worth the cost's share.</param>
/// <param name="GiveOrders">Taken by units bought from now on; those in the field keep theirs.</param>
/// <param name="Announcement">Blank clears it.</param>
/// <param name="Intel">Blank clears it.</param>
public readonly record struct KsFieldCommanderAssetDetailsEdit(
    float? StandOff = null,
    int? KillPoints = null,
    bool? GiveOrders = null,
    string? HumanoidName = null,
    string? Announcement = null,
    string? Intel = null);

/// <summary>
///     A change to when an asset comes whatever the budget buys, and to its tags; every field left null stays as it
///         is. Tags, when given, replace the asset's.
/// </summary>
public readonly record struct KsFieldCommanderAssetScheduleEdit(
    int? GuaranteedEveryWaves = null,
    bool? GuaranteedOnPhaseEnter = null,
    IReadOnlyList<string>? Tags = null);
