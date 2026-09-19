using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Server._KS14.NPC.Systems;
using Robust.Shared.Random;

namespace Content.Server._KS14.NPC.Operators;

/// <summary>
/// Selects a weighted random attack from a weight table (optionally modified
/// by anger), then executes it with its speech line — all atomically.
/// </summary>
[DataDefinition]
public sealed partial class WeightedRangedOperator : HTNOperator
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chat = default!;

    /// <summary>Attack id -> base weight.</summary>
    [DataField("weights")] public Dictionary<string, float> Weights = new();

    /// <summary>Attack id -> additional weight per point of anger.</summary>
    [DataField("angerWeights")] public Dictionary<string, float> AngerWeights = new();

    /// <summary>Attack id -> speech line spoken when that attack fires.</summary>
    [DataField("speeches")] public Dictionary<string, string> Speeches = new();

    /// <summary>Base probability (0-100) that any attack fires at all this plan.</summary>
    [DataField("probability")] public float Probability = 100f;

    [DataField("useAngerModifier")] public bool UseAngerModifier = false;

    // Unique-per-instance blackboard keys to avoid clashes between operators.
    private string ResolvedKey => "WeightedResolved." + GetHashCode();
    private string ExecutedKey => "WeightedExecuted." + GetHashCode();

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        blackboard.SetValue(ResolvedKey, "");
        blackboard.SetValue(ExecutedKey, false);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var attackSystem = _entMan.System<NPCCombatRangedPatternSystem>();

        // Already fired this plan - fire-and-forget.
        if (blackboard.ContainsKey(ExecutedKey) && blackboard.GetValue<bool>(ExecutedKey))
            return HTNOperatorStatus.Finished;

        EntityUid? target = null;
        if (blackboard.ContainsKey("Target"))
            target = blackboard.GetValue<EntityUid>("Target");

        // Optional probability gate.
        if (Probability < 100f && !_random.Prob(Probability / 100f))
            return HTNOperatorStatus.Failed;

        // Roll ONCE among the weighted attacks, then attempt that single attack.
        // Never re-roll in a loop: when everything is on cooldown the roll is
        // stateless and a retry loop would spin forever, wedging the server.
        var attackId = PickWeighted(owner);
        if (attackId == null)
            return HTNOperatorStatus.Failed;

        if (!attackSystem.ExecuteAttack(owner, attackId, target))
            return HTNOperatorStatus.Failed; // on cooldown -> plan fails -> replan later

        if (Speeches.TryGetValue(attackId, out var speech))
            _chat.TrySendInGameICMessage(owner, speech, InGameICChatType.Speak,
                false, true, checkRadioPrefix: false);

        blackboard.SetValue(ResolvedKey, attackId);
        blackboard.SetValue(ExecutedKey, true);
        return HTNOperatorStatus.Finished;
    }

    /// <summary>Effective weight = base + anger scaling. The single source of truth for weight math.</summary>
    private float GetWeight(string key, float anger)
    {
        var weight = Weights.GetValueOrDefault(key);
        if (AngerWeights.TryGetValue(key, out var perAnger))
            weight += anger * perAnger;
        return weight;
    }

    private string? PickWeighted(EntityUid owner)
    {
        var anger = UseAngerModifier
            ? _entMan.System<NPCAngerModifierSystem>().GetAngerModifier(owner)
            : 0f;

        var keys = new string?[Weights.Count];
        var cumulative = new float[Weights.Count];

        var total = 0f;
        var count = 0;
        foreach (var key in Weights.Keys)
        {
            var weight = GetWeight(key, anger);
            if (weight <= 0f)
                continue;

            total += weight;
            keys[count] = key;
            cumulative[count] = total;
            count++;
        }

        if (count == 0)
            return null;

        var roll = _random.NextFloat() * total;

        // First entry whose cumulative total passes the roll.
        for (var i = 0; i < count; i++)
        {
            if (roll < cumulative[i])
                return keys[i]!;
        }

        return keys[count - 1]!; // float-edge safety; returns a legal entry
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        blackboard.SetValue(ResolvedKey, "");
        blackboard.SetValue(ExecutedKey, false);
    }
}
