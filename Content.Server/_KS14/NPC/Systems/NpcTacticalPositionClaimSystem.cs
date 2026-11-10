using Content.Server._KS14.NPC.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Systems;

/// <summary>
/// Reservation table preventing NPCs using <see cref="Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.TacticalPositionOperator"/>
/// from converging on the same dynamically-picked camping/retreat/advance position. Backed by
/// <see cref="NpcTacticalPositionClaimComponent"/> rather than a system-owned lookup table: a claim is then
/// entity-lifetime-bound for free (deleting the owning NPC removes its claim automatically, no leak to sweep),
/// and reading every live claim reuses the same query enumerator every other NPC subsystem iterates with. Nothing
/// ticks them: a claim past its expiry is skipped wherever claims are read, and replaced or removed by the next
/// <see cref="Claim"/> or <see cref="ReleaseClaim"/>.
/// </summary>
public sealed partial class NpcTacticalPositionClaimSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<NpcTacticalPositionClaimComponent> _claimQuery = default!;

    /// <summary>
    /// Registers (or refreshes) a claim on behalf of <paramref name="owner"/>.
    /// </summary>
    public void Claim(EntityUid owner, EntityCoordinates coordinates, TimeSpan ttl, float clearanceRadius)
    {
        var claim = EnsureComp<NpcTacticalPositionClaimComponent>(owner);
        claim.Coordinates = coordinates;
        claim.ExpiresAt = _gameTiming.CurTime + ttl;
        claim.ClearanceRadius = clearanceRadius;
    }

    /// <summary>
    /// Releases <paramref name="owner"/>'s claim early, if any. Called from
    /// <see cref="Content.Server.NPC.HTN.IHtnConditionalShutdown"/>/<c>TaskShutdown</c>; one never released just
    /// expires.
    /// </summary>
    public void ReleaseClaim(EntityUid owner)
    {
        RemComp<NpcTacticalPositionClaimComponent>(owner);
    }

    /// <summary>
    /// Where <paramref name="owner"/> has claimed, if its claim has not expired.
    /// </summary>
    public bool TryGetClaim(Entity<NpcTacticalPositionClaimComponent?> owner, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (!_claimQuery.Resolve(owner.Owner, ref owner.Comp, logMissing: false) || !IsLive(owner.Comp))
            return false;

        coordinates = owner.Comp.Coordinates;
        return true;
    }

    private bool IsLive(NpcTacticalPositionClaimComponent claim)
    {
        return _gameTiming.CurTime < claim.ExpiresAt;
    }

    /// <summary>
    /// Returns a penalty multiplier in [0, 1] for how "claimed" the given candidate position is, considering
    /// every live claim on the same grid within its (or the caller's) clearance radius. 1 = unclaimed/clear,
    /// 0 = coincides with a live claim. <paramref name="ignoreClaimantUid"/>'s own claim, if given, is left out.
    /// </summary>
    public float GetClaimPenalty(EntityCoordinates candidate, float clearanceRadius, EntityUid? ignoreClaimantUid = null)
    {
        var candidateMap = _transformSystem.ToMapCoordinates(candidate);
        var penalty = 1f;

        var query = EntityQueryEnumerator<NpcTacticalPositionClaimComponent>();
        while (query.MoveNext(out var claimantUid, out var claim))
        {
            if (claimantUid == ignoreClaimantUid || !IsLive(claim))
                continue;

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
        var claims = new List<(EntityCoordinates, float)>();
        var query = EntityQueryEnumerator<NpcTacticalPositionClaimComponent>();

        while (query.MoveNext(out _, out var claim))
        {
            if (IsLive(claim))
                claims.Add((claim.Coordinates, claim.ClearanceRadius));
        }

        return claims;
    }
}
