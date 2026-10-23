using Content.Server._KS14.NPC.AttackPatterns;
using Content.Server._KS14.NPC.Meters;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Random.Helpers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Combat;

/// <summary>
///     Picks one of <see cref="Attacks"/> at random by weight, weighted further by <see cref="WeightMeter"/> if set,
///         and starts it with its speech line. Finishes as soon as the attack
///         starts; the attack then fires by itself. See <see cref="NpcCombatRangedPatternSystem"/>.
/// </summary>
public sealed partial class WeightedRangedOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private NpcMeterSystem _npcMeterSystem = default!;
    [Dependency] private NpcCombatRangedPatternSystem _npcCombatRangedPatternSystem = default!;

    [DataField(required: true)]
    public List<WeightedRangedAttack> Attacks = new();

    /// <summary>
    ///     A meter on the NPC, such as anger, whose value adds <see cref="WeightedRangedAttack.MeterWeight"/> per point
    ///         to each attack's weight.
    /// </summary>
    [DataField]
    public ProtoId<NpcMeterPrototype>? WeightMeter;

    /// <summary>
    ///     Chance, from 0 to 100, that an attack is tried at all.
    /// </summary>
    [DataField]
    public float Probability = 100f;

    /// <summary>
    ///     If false, an attack that can't fire (probability roll, cooldown) finishes the task instead of
    ///         failing the plan, so tasks after this one (e.g. moving to the target) still run.
    /// </summary>
    [DataField]
    public bool FailIfNotFired = true;

    /// <summary>
    ///     Blackboard key of the entity to aim at. Optional; attacks that need a target don't fire without one.
    /// </summary>
    [DataField]
    public string TargetKey = "Target";

    /// <summary>
    ///     Scratch for the weights of one pick, by index into <see cref="Attacks"/>. Operators are shared between NPCs,
    ///         but only ever updated one at a time.
    /// </summary>
    private readonly Dictionary<int, float> _effectiveWeights = new();

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var notFiredStatus = FailIfNotFired ? HTNOperatorStatus.Failed : HTNOperatorStatus.Finished;

        if (Probability < 100f && !_random.Prob(Probability / 100f))
            return notFiredStatus;

        // Roll once and try only that attack. Re-rolling until one fires would spin forever with everything on cooldown.
        if (PickWeighted(owner) is not { } attack)
            return notFiredStatus;

        EntityUid? targetUid = blackboard.TryGetValue<EntityUid>(TargetKey, out var foundTargetUid, _entityManager)
            ? foundTargetUid
            : null;

        if (!_npcCombatRangedPatternSystem.TryStartAttack(owner, attack.Attack, targetUid, speech: attack.Speech))
            return notFiredStatus;

        return HTNOperatorStatus.Finished;
    }

    private WeightedRangedAttack? PickWeighted(EntityUid owner)
    {
        var meterValue = WeightMeter is { } weightMeter ? _npcMeterSystem.GetValue(owner, weightMeter) : 0f;

        _effectiveWeights.Clear();
        for (var i = 0; i < Attacks.Count; i++)
        {
            var weight = Attacks[i].Weight + meterValue * Attacks[i].MeterWeight;
            if (weight > 0f)
                _effectiveWeights[i] = weight;
        }

        return _effectiveWeights.Count == 0 ? null : Attacks[_random.Pick(_effectiveWeights)];
    }
}

/// <summary>
///     One attack <see cref="WeightedRangedOperator"/> may pick.
/// </summary>
[DataDefinition]
public sealed partial class WeightedRangedAttack
{
    [DataField(required: true)]
    public EntProtoId<NpcRangedAttackPatternComponent> Attack;

    [DataField]
    public float Weight = 1f;

    /// <summary>
    ///     Weight gained per point of the operator's <see cref="WeightedRangedOperator.WeightMeter"/>.
    /// </summary>
    [DataField]
    public float MeterWeight;

    /// <summary>
    ///     Line spoken when the attack starts.
    /// </summary>
    [DataField]
    public string? Speech;
}
