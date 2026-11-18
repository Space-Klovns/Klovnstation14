namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Guaranteed assets

    /// <summary>
    ///     Whether an asset is guaranteed a place in the wave about to be sent: its
    ///         <see cref="KsFieldCommanderAsset.GuaranteedEveryWaves"/> has come round, or it is the first wave of a
    ///         phase and it is <see cref="KsFieldCommanderAsset.GuaranteedOnPhaseEnter"/>.
    /// </summary>
    /// <param name="waveNumber">The round's wave about to be sent, counting from 1.</param>
    public static bool IsGuaranteed(KsFieldCommanderRuleComponent component, KsFieldCommanderAsset asset, int waveNumber)
    {
        if (asset.Disabled || asset.MinPhase > component.PhaseIndex)
            return false;

        if (asset.GuaranteedEveryWaves > 0 && waveNumber > 0 && waveNumber % asset.GuaranteedEveryWaves == 0)
            return true;

        return asset.GuaranteedOnPhaseEnter && component.PhaseWavesSent == 0;
    }

    /// <summary>
    ///     Adds one purchase of every asset guaranteed this wave to <paramref name="purchases"/>, as long as it fits
    ///         under <paramref name="capacity"/> and its own alive cap, and takes their room from both. Returns what they
    ///         cost, which the caller charges to the wave's budget whatever the budget was.
    /// </summary>
    public float BuyGuaranteedAssets(KsFieldCommanderRuleComponent component,
        int waveNumber,
        ref int capacity,
        Dictionary<int, int> aliveByAsset,
        List<int> purchases)
    {
        var spent = 0f;

        for (var i = 0; i < component.Assets.Count; i++)
        {
            var asset = component.Assets[i];
            if (!IsGuaranteed(component, asset, waveNumber) ||
                !CanFieldAsset(asset, i, capacity, aliveByAsset))
                continue;

            spent += MathF.Max(asset.Cost, 0f);
            capacity -= asset.Count;
            aliveByAsset[i] = aliveByAsset.GetValueOrDefault(i) + asset.Count;
            purchases.Add(i);
        }

        return spent;
    }

    /// <summary>
    ///     Whether one more purchase of an asset keeps under the field's room and the asset's own alive cap.
    /// </summary>
    private static bool CanFieldAsset(KsFieldCommanderAsset asset, int assetIndex, int capacity, Dictionary<int, int> aliveByAsset)
    {
        if (asset.Count > capacity)
            return false;

        return asset.MaxAlive <= 0 || aliveByAsset.GetValueOrDefault(assetIndex) + asset.Count <= asset.MaxAlive;
    }

    #endregion
}
