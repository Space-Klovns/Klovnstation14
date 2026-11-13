using System.Numerics;
using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC.Components;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Squad;

/// <summary>
///     The other side of a fire lane: one who would throw itself across it. A shooter's check (<see cref="GatherFriends"/>)
///         allows for a friend walking, or swerving, into the way of a round already flying; a dive is many times faster,
///         and decided by the diver's HTN (retreating, getting away from a grenade) at any moment, after a round fired
///         clear of it is on its way. Allowing for that at the shooter blocks every direction a friend could dive across,
///         which near enough stops a squad with a diver in it firing at all. So the diver asks instead, before it goes:
///         <see cref="CrossesFriendlyFire"/>, from <c>DiveOperator</c>.
/// </summary>
public sealed partial class NpcSquadFireLaneSystem
{
    [Dependency] private NpcGunfireSystem _npcGunfireSystem = default!;

    /// <summary>
    ///     How recently a friend must have fired for its line of fire to count: a gun in the middle of a burst, or between
    ///         two shots at the same hostile.
    /// </summary>
    private static readonly TimeSpan DiveFireMemory = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How near a line of fire a dive may pass, beyond the diver's body, given the gun's spread at range.
    /// </summary>
    private const float DiveFireMargin = 0.5f;

    /// <summary>
    ///     Whether <paramref name="uid"/> going from <paramref name="from"/> to <paramref name="to"/> would cross the line of
    ///         fire of a friendly NPC that is shooting: one that fired within <see cref="DiveFireMemory"/>, at its target and
    ///         on past it as far as a miss flies. Never while fire lanes are off.
    /// </summary>
    public bool CrossesFriendlyFire(EntityUid uid, MapCoordinates from, MapCoordinates to)
    {
        if (!Enabled || from.MapId != to.MapId)
            return false;

        var now = _gameTiming.CurTime;
        var clearance = GetBodyRadius(uid) + DiveFireMargin;

        // Every NPC gunner rather than a lookup: there are few of them, and dives are rare.
        var enumerator = EntityQueryEnumerator<NPCRangedCombatComponent, TransformComponent>();
        while (enumerator.MoveNext(out var shooterUid, out var rangedCombatComponent, out var shooterTransformComponent))
        {
            if (shooterUid == uid ||
                shooterTransformComponent.MapID != from.MapId ||
                !_npcGunfireSystem.TryGetLastShot(shooterUid, out var lastShotAt) ||
                now - lastShotAt > DiveFireMemory ||
                TerminatingOrDeleted(rangedCombatComponent.Target))
                continue;

            var shooterPosition = _transformSystem.GetWorldPosition(shooterTransformComponent);
            if ((shooterPosition - from.Position).LengthSquared() > MissReach * MissReach)
                continue;

            var targetMapCoordinates = _transformSystem.GetMapCoordinates(rangedCombatComponent.Target);
            var aim = targetMapCoordinates.Position - shooterPosition;
            if (targetMapCoordinates.MapId != from.MapId || aim.LengthSquared() < 0.0001f)
                continue;

            var fireEnd = shooterPosition + Vector2.Normalize(aim) * MissReach;
            if (GetSegmentDistance(from.Position, to.Position, shooterPosition, fireEnd) <= clearance &&
                _npcFactionSystem.IsEntityFriendly(shooterUid, uid))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     The least distance between segments <paramref name="a"/>-<paramref name="b"/> and <paramref name="c"/>-<paramref name="d"/>:
    ///         0 if they cross.
    /// </summary>
    private static float GetSegmentDistance(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var ab = b - a;
        var cd = d - c;
        var denominator = ab.X * cd.Y - ab.Y * cd.X;

        if (MathF.Abs(denominator) > 1e-6f)
        {
            var ac = c - a;
            var t = (ac.X * cd.Y - ac.Y * cd.X) / denominator;
            var u = (ac.X * ab.Y - ac.Y * ab.X) / denominator;
            if (t is >= 0f and <= 1f && u is >= 0f and <= 1f)
                return 0f;
        }

        return MathF.Min(
            MathF.Min(GetPointSegmentDistance(a, c, d), GetPointSegmentDistance(b, c, d)),
            MathF.Min(GetPointSegmentDistance(c, a, b), GetPointSegmentDistance(d, a, b)));
    }

    private static float GetPointSegmentDistance(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        var t = lengthSquared > 0f ? Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0f, 1f) : 0f;
        return Vector2.Distance(point, start + segment * t);
    }
}
