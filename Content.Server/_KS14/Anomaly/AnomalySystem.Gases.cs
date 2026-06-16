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
        public float LastSentScalingFactor = -1f;
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

            // Find dominant gas
            Gas? dominantGas = null;
            float maxMoles = 0;

            foreach (var gas in _gasValues)
            {
                var moles = mixture.GetMoles(gas);
                if (moles > maxMoles)
                {
                    maxMoles = moles;
                    dominantGas = gas;
                }
            }

            if (dominantGas == null || dominantGas == Gas.Nitrogen)
            {
                ResetGasState(uid, consumer, state);
                continue;
            }

            // Calculate partial pressure directly from the mixture's total pressure
            var partialPressure = mixture.Pressure * (maxMoles / mixture.TotalMoles);

            if (partialPressure < consumer.MinPressureThreshold)
            {
                ResetGasState(uid, consumer, state);
                continue;
            }

            if (!_gasEffects.TryGetValue(dominantGas.Value, out var effect))
            {
                ResetGasState(uid, consumer, state);
                // Log warning for missing non-inert prototypes
                Log.Warning($"Anomaly {ToPrettyString(uid)} is in {dominantGas.Value} but no AnomalyGasEffectPrototype is defined for it.");
                continue;
            }

            // Scaling calculation
            var scaling = Math.Clamp((partialPressure - consumer.MinPressureThreshold) / (consumer.MaxPressureCap - consumer.MinPressureThreshold), 0f, 1f);

            // Apply continuous modifiers
            if (effect.StabilityModifier != 0)
                ChangeAnomalyStability(uid, effect.StabilityModifier * scaling * elapsed, anomaly);

            if (effect.SeverityModifier != 0)
                ChangeAnomalySeverity(uid, effect.SeverityModifier * scaling * elapsed, anomaly);

            if (effect.HealthModifier != 0)
                ChangeAnomalyHealth(uid, effect.HealthModifier * scaling * elapsed, anomaly);

            // Update multipliers for shared logic
            consumer.PointMultiplier = 1f + (effect.PointMultiplier - 1f) * scaling;
            consumer.PulseFrequencyMultiplier = 1f + (effect.PulseFrequencyMultiplier - 1f) * scaling;
            consumer.DecayBuffer = 1f + (effect.DecayBuffer - 1f) * scaling;
            consumer.PulsePowerMultiplier = 1f + (effect.PulsePowerMultiplier - 1f) * scaling;

            // Consume gas
            var consumed = consumer.ConsumptionRate * scaling * elapsed;
            mixture.AdjustMoles(dominantGas.Value, -consumed);

            // Networking threshold check
            var gasChanged = consumer.ActiveGas != dominantGas;
            var scalingThresholdMet = Math.Abs(scaling - state.LastSentScalingFactor) >= 0.1f;

            if (gasChanged || scalingThresholdMet)
            {
                consumer.ActiveGas = dominantGas;
                consumer.ScalingFactor = scaling;
                state.LastSentScalingFactor = scaling;
                Dirty(uid, consumer);
            }
        }
    }

    private void ResetGasState(EntityUid uid, AnomalyGasConsumerComponent consumer, GasConsumerProcessingState state)
    {
        if (consumer.ActiveGas == null && state.LastSentScalingFactor == -1f)
            return;

        consumer.ActiveGas = null;
        consumer.ScalingFactor = 0f;
        state.LastSentScalingFactor = -1f;

        consumer.PointMultiplier = 1f;
        consumer.PulseFrequencyMultiplier = 1f;
        consumer.DecayBuffer = 1f;
        consumer.PulsePowerMultiplier = 1f;

        Dirty(uid, consumer);
    }
}
