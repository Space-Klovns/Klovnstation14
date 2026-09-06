using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Server._KS14.NPC.Systems;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Operators;

/// <summary>
/// Executes an NPC ranged pattern attack by ID. All state lives in the
/// blackboard so operators remain safe across NPCs, replans, and plan branches.
/// Optionally speaks a line atomically with the attack start.
/// </summary>
[DataDefinition]
public sealed partial class RangedOperator : HTNOperator
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ChatSystem _chat = default!;

    /// <summary>
    /// Explicit attack to execute. Leave blank to pull the attack id from the
    /// blackboard key SelectedAttackKey instead.
    /// </summary>
    [DataField("attackId")] public string AttackId = "";

    /// <summary>
    /// Blackboard key to read the attack id from when AttackId is blank.
    /// Should match the targetKey of whatever selected the attack.
    /// </summary>
    [DataField("selectedAttackKey")] public string SelectedAttackKey = "SelectedAttack";

    /// <summary>
    /// Speech line to say when the attack starts (synced with the attack —
    /// either both happen or neither). Leave empty for no speech.
    /// </summary>
    [DataField("speech")] public string Speech = "";

    /// <summary>
    /// Blackboard bool key set to true when this attack fires.
    /// Use it to make one-shot attacks (e.g. the final) never repeat.
    /// Leave empty for no flag.
    /// </summary>
    [DataField("executedFlagKey")] public string ExecutedFlagKey = "";

    // Blackboard keys we use internally (unique per-operator instance to avoid clashes)
    private string ExecutedKey => "RangedExecuted." + GetHashCode();
    private string ResolvedKey => "RangedResolved." + GetHashCode();

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        var resolved = !string.IsNullOrEmpty(AttackId)
            ? AttackId
            : blackboard.ContainsKey(SelectedAttackKey)
                ? blackboard.GetValue<string>(SelectedAttackKey)
                : "";

        blackboard.SetValue(ResolvedKey, resolved);
        blackboard.SetValue(ExecutedKey, false);

        // One-shot guard: if this attack has already fired (flag set),
        // resolve to nothing so Update fails immediately.
        if (!string.IsNullOrEmpty(ExecutedFlagKey) &&
            blackboard.ContainsKey(ExecutedFlagKey) &&
            blackboard.GetValue<bool>(ExecutedFlagKey))
        {
            blackboard.SetValue(ResolvedKey, "");
        }
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        // Target is optional (some attacks don't need one) - read defensively.
        EntityUid? target = null;
        if (blackboard.ContainsKey("Target"))
            target = blackboard.GetValue<EntityUid>("Target");

        var attackSystem = _entMan.System<NPCCombatRangedPatternSystem>();
        var attackId = blackboard.ContainsKey(ResolvedKey)
            ? blackboard.GetValue<string>(ResolvedKey)
            : "";

        // No attack resolved (e.g. one-shot already used) - fail this branch.
        if (string.IsNullOrEmpty(attackId))
            return HTNOperatorStatus.Failed;

        var executed = blackboard.ContainsKey(ExecutedKey) &&
                       blackboard.GetValue<bool>(ExecutedKey);

        if (!executed)
        {
            if (!attackSystem.ExecuteAttack(owner, attackId, target))
                return HTNOperatorStatus.Failed;   // on cooldown -> branch fails -> move branch

            if (!string.IsNullOrEmpty(Speech))
                _chat.TrySendInGameICMessage(owner, Speech, InGameICChatType.Speak,
                    false, true, checkRadioPrefix: false);

            if (!string.IsNullOrEmpty(ExecutedFlagKey))
                blackboard.SetValue(ExecutedFlagKey, true);

            blackboard.SetValue(ExecutedKey, true);

            // Don't wait for the cooldown - finish immediately so the planner can
            // pick movement or another attack on the next replan. ExecuteAttack's
            // cooldown guard prevents double-firing.
            return HTNOperatorStatus.Finished;
        }
        if (attackSystem.IsAttackActive(owner))
            return HTNOperatorStatus.Continuing;


        return HTNOperatorStatus.Finished;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);

        // Clean up our keys so a replan doesn't inherit stale state.
        // Remove<T> isn't available in all versions - SetValue to defaults instead.
        blackboard.SetValue(ResolvedKey, "");
        blackboard.SetValue(ExecutedKey, false);
    }
}
