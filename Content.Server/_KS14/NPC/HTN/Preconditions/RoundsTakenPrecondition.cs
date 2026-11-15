using Content.Server._KS14.NPC.Suppression;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared.Destructible;
using Content.Shared.FixedPoint;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Met while what is at <see cref="TargetKey"/> is still worth shooting to break: it can be broken, for no more than
///         <see cref="MaxDamage"/>, and NPCs have fired fewer than <see cref="MaxRounds"/> rounds at it so far (all of
///         them together, through <c>SuppressOperator.barrierKey</c>). The same checks as for glass in a squad plan
///         (<c>NpcSquadTacticsSettings.PlanBreakDamage</c>), with a round count on top, so that a locker that soaks up fire is
///         given up on rather than emptied into. Recheck it on the task that fires, for that to stop the firing.
/// </summary>
public sealed partial class RoundsTakenPrecondition : KsInvertiblePrecondition
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcSuppressiveFireSystem _npcSuppressiveFireSystem = default!;
    [Dependency] private SharedDestructibleSystem _destructibleSystem = default!;

    [DataField(required: true)]
    public string TargetKey = default!;

    /// <summary>
    ///     The most rounds fired at it before it is given up on. Read from <see cref="MaxRoundsKey"/> if that is on the
    ///         blackboard.
    /// </summary>
    [DataField]
    public int MaxRounds = 60;

    /// <summary>
    ///     A blackboard float (<c>!type:Single</c>) that overrides <see cref="MaxRounds"/>, for it to be set per NPC.
    /// </summary>
    [DataField]
    public string? MaxRoundsKey;

    /// <summary>
    ///     The most damage it may take to break for it to be worth shooting: more, and it is given up on before a round is
    ///         fired. A security locker breaks at 150 (some 30 rifle rounds at point blank, through its armour), an ordinary
    ///         one at 100.
    /// </summary>
    [DataField]
    public float MaxDamage = 200f;

    protected override bool IsMetRaw(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var targetUid, _entityManager) ||
            _entityManager.Deleted(targetUid))
            return false;

        var maxRounds = MaxRoundsKey is not null && blackboard.TryGetValue<float>(MaxRoundsKey, out var keyRounds, _entityManager)
            ? keyRounds
            : MaxRounds;

        if (_npcSuppressiveFireSystem.GetRoundsTaken(targetUid) >= maxRounds)
            return false;

        return _destructibleSystem.TryGetDestroyedAt(targetUid, out var destroyedAt) &&
            destroyedAt is { } destroyedAtValue &&
            destroyedAtValue != FixedPoint2.MaxValue &&
            (float) destroyedAtValue <= MaxDamage;
    }
}
