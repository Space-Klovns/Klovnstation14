using System.Diagnostics.CodeAnalysis;
using Content.Shared.GameTicking.Components;

namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     What the field commander admin window needs on top of the admin editing API.
/// </summary>
public sealed partial class KsFieldCommanderRuleSystem
{
    #region Admin window

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

    #endregion
}
