using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     How much punishment a cultivator can take. Qi refines the body a little with every realm: a Nascent Soul goes down
///         to roughly twice what drops a mortal. The body path tempers flesh itself and stands well above that: its stages
///         and powers raise the same thresholds much further (see <see cref="SharedBodyCultivationSystem"/>). Neither is
///         unkillable: a crew that gangs up still brings them down.
/// </summary>
public sealed partial class CultivationDurabilitySystem : EntitySystem
{
    private static readonly string[] ToughenedTypes = { "Blunt", "Slash", "Piercing", "Heat", "Cold", "Shock", "Caustic" };

    [Dependency] private MobThresholdSystem _mobThresholdSystem = default!;
    [Dependency] private EntityQuery<BodyCultivatorComponent> _bodyCultivatorQuery = default!;
    [Dependency] private EntityQuery<CultivatorComponent> _cultivatorQuery = default!;
    [Dependency] private EntityQuery<CultivationDurabilityComponent> _durabilityQuery = default!;

    /// <summary>
    ///     Health a qi cultivator gains by realm, added to the thresholds for critical condition and death.
    /// </summary>
    public static float QiHealthBonus(CultivationRealm realm)
    {
        return realm switch
        {
            CultivationRealm.QiCondensation => 10f,
            CultivationRealm.FoundationEstablishment => 25f,
            CultivationRealm.GoldenCore => 40f,
            _ => 60f,
        };
    }

    /// <summary>
    ///     What share of brute and burn damage a qi cultivator still takes by realm.
    /// </summary>
    public static float QiDamageMultiplier(CultivationRealm realm)
    {
        return realm switch
        {
            CultivationRealm.QiCondensation => 1f,
            CultivationRealm.FoundationEstablishment => 0.95f,
            CultivationRealm.GoldenCore => 0.9f,
            _ => 0.85f,
        };
    }

    /// <summary>
    ///     The mob's thresholds before any cultivation, recorded the first time they're asked for.
    /// </summary>
    public (float? Critical, float? Dead) BaseThresholds(EntityUid uid)
    {
        if (!_durabilityQuery.TryComp(uid, out var durability))
        {
            durability = AddComp<CultivationDurabilityComponent>(uid);
            // Something with no health thresholds at all (not a mob) has nothing to make tougher.
            if (TryComp<MobThresholdsComponent>(uid, out var thresholds))
            {
                durability.BaseCritical = _mobThresholdSystem.TryGetThresholdForState(uid, MobState.Critical, out var critical, thresholds)
                    ? critical.Value.Float()
                    : null;
                durability.BaseDead = _mobThresholdSystem.TryGetThresholdForState(uid, MobState.Dead, out var dead, thresholds)
                    ? dead.Value.Float()
                    : null;
            }
        }

        return (durability.BaseCritical, durability.BaseDead);
    }

    /// <summary>
    ///     Set the thresholds for critical condition and death from both paths' bonuses.
    /// </summary>
    public void Refresh(EntityUid uid)
    {
        var (critical, dead) = BaseThresholds(uid);
        var bonus = 0f;
        if (_bodyCultivatorQuery.TryComp(uid, out var body))
            bonus += body.HealthBonus;
        if (_cultivatorQuery.TryComp(uid, out var cultivator))
            bonus += QiHealthBonus(cultivator.Realm);

        if (critical is { } baseCritical)
            _mobThresholdSystem.SetMobStateThreshold(uid, FixedPoint2.New(baseCritical + bonus), MobState.Critical);
        if (dead is { } baseDead)
            _mobThresholdSystem.SetMobStateThreshold(uid, FixedPoint2.New(baseDead + bonus), MobState.Dead);
    }

    [SubscribeLocalEvent]
    private void OnQiDamageModify(Entity<CultivatorComponent> entity, ref DamageModifyEvent args)
    {
        var multiplier = QiDamageMultiplier(entity.Comp.Realm);
        if (multiplier >= 1f)
            return;

        foreach (var type in ToughenedTypes)
        {
            if (args.Damage.DamageDict.TryGetValue(type, out var amount) && amount > 0)
                args.Damage.DamageDict[type] = amount * multiplier;
        }
    }
}

/// <summary>
///     A cultivator's thresholds before any cultivation raised them.
/// </summary>
[RegisterComponent]
public sealed partial class CultivationDurabilityComponent : Component
{
    [DataField]
    public float? BaseCritical;

    [DataField]
    public float? BaseDead;
}
