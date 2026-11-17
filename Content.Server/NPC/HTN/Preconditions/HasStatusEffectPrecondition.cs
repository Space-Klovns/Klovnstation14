using Content.Shared.StatusEffectNew;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing; // KS14

namespace Content.Server.NPC.HTN.Preconditions;

/// <summary>
/// Returns true if entity have specified status effect
/// </summary>
public sealed partial class HasStatusEffectPrecondition : HTNPrecondition
{
    private StatusEffectsSystem _statusEffects = default!;

    [Dependency] private IGameTiming _gameTiming = default!; // KS14

    [DataField(required: true)]
    public EntProtoId StatusEffect;

    // KS14
    /// <summary>
    ///     If set, only with at least this long of it left: a stun from being beaten down, and not a moment on the floor.
    ///         One that never ends counts.
    /// </summary>
    [DataField]
    public TimeSpan? MinRemaining;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _statusEffects =  sysManager.GetEntitySystem<StatusEffectsSystem>();
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        // KS14 start: asked first, as a mob that has never had a status effect has no container for TryGetTime to find, and it logs that
        if (!_statusEffects.HasStatusEffect(owner, StatusEffect))
            return false;

        return MinRemaining is not { } minRemaining ||
            _statusEffects.TryGetTime(owner, StatusEffect, out var time) &&
            (time.EndEffectTime is not { } endTime || endTime - _gameTiming.CurTime >= minRemaining);
        // KS14 end
    }
}
