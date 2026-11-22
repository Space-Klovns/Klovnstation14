using Robust.Shared.Random;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Doctrines

    /// <summary>
    ///     Picks the doctrine for the next wave by weight, from those available in the current phase. -1 for none.
    /// </summary>
    public int PickDoctrine(KsFieldCommanderRuleComponent component)
    {
        var totalWeight = 0f;
        foreach (var doctrine in component.Doctrines)
        {
            if (IsDoctrineAvailable(component, doctrine))
                totalWeight += doctrine.Weight;
        }

        if (totalWeight <= 0f)
            return -1;

        var roll = _robustRandom.NextFloat() * totalWeight;
        var last = -1;
        for (var i = 0; i < component.Doctrines.Count; i++)
        {
            var doctrine = component.Doctrines[i];
            if (!IsDoctrineAvailable(component, doctrine))
                continue;

            last = i;
            roll -= doctrine.Weight;
            if (roll <= 0f)
                return i;
        }

        return last;
    }

    private static bool IsDoctrineAvailable(KsFieldCommanderRuleComponent component, KsFieldCommanderDoctrine doctrine)
    {
        return doctrine.MinPhase <= component.PhaseIndex && doctrine.Weight > 0f && float.IsFinite(doctrine.Weight);
    }

    /// <summary>
    ///     An asset's weight in the weighted pick, as <paramref name="doctrine"/> would have it.
    /// </summary>
    public static float GetAssetWeight(KsFieldCommanderAsset asset, KsFieldCommanderDoctrine? doctrine)
    {
        var weight = asset.Weight;
        if (doctrine == null || doctrine.TagWeights.Count == 0)
            return weight;

        foreach (var tag in asset.Tags)
        {
            if (doctrine.TagWeights.TryGetValue(tag, out var multiplier))
                weight *= MathF.Max(0f, multiplier);
        }

        return weight;
    }

    /// <summary>
    ///     How much of a wave's budget a doctrine spends: all of it without one.
    /// </summary>
    public static float GetSpendableBudget(float budget, KsFieldCommanderDoctrine? doctrine)
    {
        if (doctrine == null || budget <= 0f)
            return budget;

        return budget * Math.Clamp(doctrine.BudgetMultiplier, 0f, 10f);
    }

    #endregion
}
