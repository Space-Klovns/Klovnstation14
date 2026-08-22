using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Systems;

/// <summary>
/// Reservation table preventing NPCs using <see cref="Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.TacticalPositionOperator"/>
/// from converging on the same dynamically-picked camping/retreat/advance position.
///
/// NOT thread-safe: every read/write happens from main-thread HTN callbacks (Plan/ConditionalShutdown/TaskShutdown)
/// or this system's own Update - never from PathfindingSystem's parallel path-processing.
/// </summary>
public sealed partial class NpcTacticalPositionClaimSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    private readonly Dictionary<EntityUid, TacticalClaim> _claims = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;
        List<EntityUid>? expired = null;

        foreach (var (owner, claim) in _claims)
        {
            if (now < claim.ExpiresAt)
                continue;

            expired ??= new List<EntityUid>();
            expired.Add(owner);
        }

        if (expired == null)
            return;

        foreach (var owner in expired)
        {
            _claims.Remove(owner);
        }
    }

    /// <summary>
    /// Registers (or refreshes) a claim on behalf of <paramref name="owner"/>.
    /// </summary>
    public void Claim(EntityUid owner, EntityCoordinates coordinates, TimeSpan ttl, float clearanceRadius)
    {
        _claims[owner] = new TacticalClaim(owner, coordinates, _gameTiming.CurTime + ttl, clearanceRadius);
    }

    /// <summary>
    /// Releases <paramref name="owner"/>'s claim early, if any. Called from
    /// <see cref="Content.Server.NPC.HTN.IHtnConditionalShutdown"/>/<c>TaskShutdown</c> as a fast path on top
    /// of the TTL sweep in <see cref="Update"/>.
    /// </summary>
    public void ReleaseClaim(EntityUid owner)
    {
        _claims.Remove(owner);
    }

    /// <summary>
    /// Returns a penalty multiplier in [0, 1] for how "claimed" the given candidate position is, considering
    /// every live claim on the same grid within its (or the caller's) clearance radius. 1 = unclaimed/clear,
    /// 0 = coincides with a live claim.
    /// </summary>
    public float GetClaimPenalty(EntityCoordinates candidate, float clearanceRadius)
    {
        if (_claims.Count == 0)
            return 1f;

        var candidateMap = _transformSystem.ToMapCoordinates(candidate);
        var penalty = 1f;

        foreach (var claim in _claims.Values)
        {
            var claimMap = _transformSystem.ToMapCoordinates(claim.Coordinates);

            if (claimMap.MapId != candidateMap.MapId)
                continue;

            var radius = MathF.Max(clearanceRadius, claim.ClearanceRadius);
            var distance = (claimMap.Position - candidateMap.Position).Length();

            if (distance >= radius)
                continue;

            // Linearly ramp the penalty down to 0 as the candidate approaches the claimed spot, rather than a
            // hard cutoff, so nearby-but-distinct candidates are merely discouraged instead of excluded outright.
            var proximity = 1f - distance / radius;
            penalty = MathF.Min(penalty, 1f - proximity);
        }

        return Math.Clamp(penalty, 0f, 1f);
    }

    /// <summary>
    /// Snapshots every live claim's coordinates and clearance radius, for debug visualization only. Callers
    /// (see <see cref="Content.Server._KS14.NPC.Systems.NpcTacticalPositionDebugSystem"/>) are expected to
    /// only call this while a debug overlay is actually subscribed, so this allocation stays off the hot path.
    /// </summary>
    public List<(EntityCoordinates Coordinates, float ClearanceRadius)> GetAllClaimsForDebug()
    {
        var claims = new List<(EntityCoordinates, float)>(_claims.Count);

        foreach (var claim in _claims.Values)
        {
            claims.Add((claim.Coordinates, claim.ClearanceRadius));
        }

        return claims;
    }

    private readonly record struct TacticalClaim(EntityUid Owner, EntityCoordinates Coordinates, TimeSpan ExpiresAt, float ClearanceRadius);
}
