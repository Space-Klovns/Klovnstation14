using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     How long ago the owner last saw (or heard called out) a hostile, as a fraction of <see cref="MaxAge"/>:
///         0 for right now, 1 for <see cref="MaxAge"/> or longer, or a hostile it knows nothing of. Pair with an
///         inverse curve to prefer the freshest.
/// </summary>
public sealed partial class ContactAgeCon : UtilityConsideration
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;

    [DataField]
    public TimeSpan MaxAge = TimeSpan.FromSeconds(30);

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        if (!_npcPerceptionSystem.TryGetContact(ownerUid, targetUid, out var contact) || MaxAge <= TimeSpan.Zero)
            return 1f;

        return Math.Clamp((float)((_gameTiming.CurTime - contact.LastSeen) / MaxAge), 0f, 1f);
    }
}
