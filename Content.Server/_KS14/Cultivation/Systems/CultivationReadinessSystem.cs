using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     One thing that helps or hurts a breakthrough (or a Tribulation of Flesh), for the breakdown shown to the player.
/// </summary>
public readonly record struct ReadinessFactor(string Key, int Amount);

/// <summary>
///     What weighs on any breakthrough, qi or body: wounds, an empty stomach, a dry throat, qi still unsettled from a
///         fight, a foundation not yet steady after the last step, and strangers close by. Heaven doesn't reward
///         breaking through in the middle of a corridor fight.
/// </summary>
public sealed partial class CultivationReadinessSystem : EntitySystem
{
    private static readonly TimeSpan UnsettledFor = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan UnsteadyFor = TimeSpan.FromMinutes(5);
    private const float DisturbanceRange = 3f;

    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private HungerSystem _hungerSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private SharedCultivationSystem _sharedCultivationSystem = default!;
    [Dependency] private ThirstSystem _thirstSystem = default!;

    private readonly HashSet<Entity<MobStateComponent>> _nearby = new();

    /// <summary>
    ///     Add the penalties every path shares.
    /// </summary>
    /// <param name="lastUse">When they last used a technique or body art.</param>
    /// <param name="lastStep">When they last broke through, or zero if never.</param>
    public void AddCommonPenalties(EntityUid uid, List<ReadinessFactor> factors, TimeSpan lastUse, TimeSpan lastStep)
    {
        var now = _gameTiming.CurTime;

        var damage = (int)(_damageableSystem.GetTotalDamage(uid).Float() / 2f);
        if (damage > 0)
            factors.Add(new ReadinessFactor("hurt", -Math.Min(20, damage)));

        if (TryComp<HungerComponent>(uid, out var hungerComponent)
            && _hungerSystem.IsHungerBelowState(uid, HungerThreshold.Peckish, comp: hungerComponent))
            factors.Add(new ReadinessFactor("hungry", -10));

        if (TryComp<ThirstComponent>(uid, out var thirstComponent) && thirstComponent.CurrentThirstThreshold <= ThirstThreshold.Thirsty)
            factors.Add(new ReadinessFactor("thirsty", -10));

        if (lastUse > TimeSpan.Zero && now - lastUse < UnsettledFor)
            factors.Add(new ReadinessFactor("unsettled", -10));

        if (lastStep > TimeSpan.Zero && now - lastStep < UnsteadyFor)
            factors.Add(new ReadinessFactor("unsteady", -15));

        var strangers = 0;
        _nearby.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(uid).Coordinates, DisturbanceRange, _nearby);
        foreach (var other in _nearby)
        {
            if (other.Owner != uid && HasComp<MindContainerComponent>(other) && _mobStateSystem.IsAlive(other, other.Comp)
                && !_sharedCultivationSystem.IsAlly(uid, other))
                strangers++;
        }

        if (strangers > 0)
            factors.Add(new ReadinessFactor("disturbed", -Math.Min(15, 5 * strangers)));
    }

    public static int Total(int baseReadiness, List<ReadinessFactor> factors)
    {
        var total = baseReadiness;
        foreach (var factor in factors)
            total += factor.Amount;

        return total;
    }

    /// <summary>
    ///     The breakdown as one line, e.g. "+10 full qi, −10 hungry, −15 strangers close by".
    /// </summary>
    public string Describe(List<ReadinessFactor> factors)
    {
        if (factors.Count == 0)
            return Loc.GetString("cultivation-readiness-factor-none");

        var parts = new List<string>();
        foreach (var factor in factors)
        {
            var sign = factor.Amount >= 0 ? "+" : "−";
            parts.Add($"{sign}{Math.Abs(factor.Amount)} {Loc.GetString($"cultivation-readiness-factor-{factor.Key}")}");
        }

        return string.Join(", ", parts);
    }
}
