using Content.Shared.StatusEffectNew.Components;

namespace Content.Client._KS14.StatusEffect;

public static class KsStatusEffectTimeLeft
{
    /// <summary>
    ///     Ratio of a status effect's total duration still left: 1 when it starts, 0 when it ends. An effect with no
    ///         end time gives 1.
    /// </summary>
    public static float GetRatio(StatusEffectComponent statusEffectComponent, TimeSpan curTime)
    {
        if (statusEffectComponent.EndEffectTime is not { } endTime)
            return 1f;

        var totalSeconds = (float)(endTime - statusEffectComponent.StartEffectTime).TotalSeconds;
        return totalSeconds > 0f
            ? Math.Clamp((float)(endTime - curTime).TotalSeconds / totalSeconds, 0f, 1f)
            : 0f;
    }
}
