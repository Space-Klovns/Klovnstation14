using Content.Shared.Anomaly.Components; // KS14
using Content.Shared._KS14.Anomaly.Components;
using Content.Shared._KS14.Anomaly.Prototypes;
using Content.Shared.Atmos;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Anomaly;

public sealed partial class AnomalySystem
{
    /// <summary>
    /// Transient cache for processing accumulators and networking thresholds.
    /// </summary>
    private sealed class GasConsumerProcessingState
    {
        public float Accumulator;
        public float LastSentPrimaryScaling = -1f;
        public float LastSentSecondaryScaling = -1f;
    }

    private readonly Dictionary<EntityUid, GasConsumerProcessingState> _processingCache = new();
    private readonly Dictionary<Gas, AnomalyGasEffectPrototype> _gasEffects = new();
    private Gas[] _gasValues = Array.Empty<Gas>();

    private void InitializeGases()
    {
        SubscribeLocalEvent<AnomalyGasConsumerComponent, ComponentShutdown>(OnGasConsumerShutdown);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        _gasValues = Enum.GetValues<Gas>();
        CacheGasEffects();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<AnomalyGasEffectPrototype>())
            CacheGasEffects();
    }

    private void CacheGasEffects()
    {
        _gasEffects.Clear();
        foreach (var proto in _prototype.EnumeratePrototypes<AnomalyGasEffectPrototype>())
        {
            _gasEffects[proto.Gas] = proto;
        }
    }

    private void OnGasConsumerShutdown(EntityUid uid, AnomalyGasConsumerComponent component, ComponentShutdown args)
    {
        _processingCache.Remove(uid);
    }

    private void UpdateGasConsumption(float frameTime)
    {
        var query = EntityQueryEnumerator<AnomalyGasConsumerComponent, AnomalyComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var consumer, out var anomaly, out var xform))
        {
            if (!_processingCache.TryGetValue(uid, out var state))
            {
                state = new GasConsumerProcessingState();
                _processingCache[uid] = state;
            }

            state.Accumulator += frameTime;
            var interval = Math.Clamp(consumer.UpdateInterval, 0.5f, 2.0f);

            if (state.Accumulator < interval)
                continue;

            var elapsed = state.Accumulator;
            state.Accumulator = 0;

            if (xform.GridUid == null)
            {
                ResetGasState(uid, consumer, state);
                continue;
            }

            var mixture = _atmosphere.GetContainingMixture(uid, false, true);
            if (mixture == null || mixture.TotalMoles <= 0)
            {
                ResetGasState(uid, consumer, state);
                continue;
            }

            // Find dominant top 2 gases
            Gas? primaryGas = null;
            float primaryMoles = 0;
            Gas? secondaryGas = null;
            float secondaryMoles = 0;

            foreach (var gas in _gasValues)
            {
                if (gas == Gas.Nitrogen)
                    continue;

                var moles = mixture.GetMoles(gas);
                if (moles <= 0) continue;

                if (moles > primaryMoles)
                {
                    secondaryGas = primaryGas;
                    secondaryMoles = primaryMoles;
                    primaryGas = gas;
                    primaryMoles = moles;
                }
                else if (moles > secondaryMoles)
                {
                    secondaryGas = gas;
                    secondaryMoles = moles;
                }
            }

            // Calculate primary partial pressure directly from the mixture's total pressure
            float primaryPressure = primaryMoles > 0 ? mixture.Pressure * (primaryMoles / mixture.TotalMoles) : 0f;
            float secondaryPressure = secondaryMoles > 0 ? mixture.Pressure * (secondaryMoles / mixture.TotalMoles) : 0f;

            if (primaryGas == null || primaryPressure < consumer.MinPressureThreshold || !_gasEffects.ContainsKey(primaryGas.Value))
            {
                ResetGasState(uid, consumer, state);
                continue;
            }

            // Process Primary
            var primaryScaling = Math.Clamp((primaryPressure - consumer.MinPressureThreshold) / (consumer.MaxPressureCap - consumer.MinPressureThreshold), 0f, 1f);
            var primaryEffect = _gasEffects[primaryGas.Value];

            // Process Secondary
            float secondaryScaling = 0f;
            AnomalyGasEffectPrototype? secondaryEffect = null;
            if (secondaryGas != null && secondaryPressure >= consumer.MinPressureThreshold && _gasEffects.TryGetValue(secondaryGas.Value, out secondaryEffect))
            {
                secondaryScaling = Math.Clamp((secondaryPressure - consumer.MinPressureThreshold) / (consumer.MaxPressureCap - consumer.MinPressureThreshold), 0f, 1f);
            }

            // Calculate total deltas and multipliers
            float stabDelta = primaryEffect.StabilityModifier * primaryScaling;
            float sevDelta = primaryEffect.SeverityModifier * primaryScaling;
            float healthDelta = primaryEffect.HealthModifier * primaryScaling;

            float ptMult = (primaryEffect.PointMultiplier - 1f) * primaryScaling;
            float freqMult = (primaryEffect.PulseFrequencyMultiplier - 1f) * primaryScaling;
            float decBuffer = (primaryEffect.DecayBuffer - 1f) * primaryScaling;
            float powMult = (primaryEffect.PulsePowerMultiplier - 1f) * primaryScaling;

            if (secondaryEffect != null && secondaryScaling > 0)
            {
                float secFactor = secondaryScaling * consumer.SecondaryGasPenalty;
                stabDelta += secondaryEffect.StabilityModifier * secFactor;
                sevDelta += secondaryEffect.SeverityModifier * secFactor;
                healthDelta += secondaryEffect.HealthModifier * secFactor;

                ptMult += (secondaryEffect.PointMultiplier - 1f) * secFactor;
                freqMult += (secondaryEffect.PulseFrequencyMultiplier - 1f) * secFactor;
                decBuffer += (secondaryEffect.DecayBuffer - 1f) * secFactor;
                powMult += (secondaryEffect.PulsePowerMultiplier - 1f) * secFactor;
            }

            // Apply continuous modifiers
            if (stabDelta != 0) ChangeAnomalyStability(uid, stabDelta * elapsed, anomaly);
            if (sevDelta != 0) ChangeAnomalySeverity(uid, sevDelta * elapsed, anomaly);
            if (healthDelta != 0) ChangeAnomalyHealth(uid, healthDelta * elapsed, anomaly);

            // Update multipliers
            consumer.PointMultiplier = 1f + ptMult;
            consumer.PulseFrequencyMultiplier = 1f + freqMult;
            consumer.DecayBuffer = 1f + decBuffer;
            consumer.PulsePowerMultiplier = 1f + powMult;

            // Consume gas
            var primaryConsumed = consumer.ConsumptionRate * primaryScaling * elapsed;
            mixture.AdjustMoles(primaryGas.Value, -primaryConsumed);

            if (secondaryEffect != null && secondaryScaling > 0)
            {
                var secondaryConsumed = consumer.ConsumptionRate * secondaryScaling * elapsed;
                mixture.AdjustMoles(secondaryGas!.Value, -secondaryConsumed);
            }

            // Networking threshold check
            var gasChanged = consumer.PrimaryGas != primaryGas || consumer.SecondaryGas != secondaryGas;
            var primaryMet = Math.Abs(primaryScaling - state.LastSentPrimaryScaling) >= 0.1f;
            var secondaryMet = Math.Abs(secondaryScaling - state.LastSentSecondaryScaling) >= 0.1f;

            if (gasChanged || primaryMet || secondaryMet)
            {
                consumer.PrimaryGas = primaryGas;
                consumer.SecondaryGas = secondaryGas;
                consumer.PrimaryScalingFactor = primaryScaling;
                consumer.SecondaryScalingFactor = secondaryScaling;

                state.LastSentPrimaryScaling = primaryScaling;
                state.LastSentSecondaryScaling = secondaryScaling;
                Dirty(uid, consumer);
            }
        }
    }

    private void ResetGasState(EntityUid uid, AnomalyGasConsumerComponent consumer, GasConsumerProcessingState state)
    {
        if (consumer.PrimaryGas == null && state.LastSentPrimaryScaling == -1f)
            return;

        consumer.PrimaryGas = null;
        consumer.SecondaryGas = null;
        consumer.PrimaryScalingFactor = 0f;
        consumer.SecondaryScalingFactor = 0f;

        state.LastSentPrimaryScaling = -1f;
        state.LastSentSecondaryScaling = -1f;

        consumer.PointMultiplier = 1f;
        consumer.PulseFrequencyMultiplier = 1f;
        consumer.DecayBuffer = 1f;
        consumer.PulsePowerMultiplier = 1f;

        Dirty(uid, consumer);
    }
}
