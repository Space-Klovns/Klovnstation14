using System.Numerics;
using Content.Server._KS14.NPC.Utility;
using Content.Shared.NPC.Systems;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     Whose fire is where: everyone that has fired lately (<see cref="NpcRecentShooterComponent"/>, players and NPCs alike)
///         has a line of fire for each of its last few shots, from where it fired along where it aimed, until something
///         anchored that its round collides with stops it: a laser goes on through a window, a bullet does not (the mask
///         is <c>NpcLineOfFireSystem</c>'s, what NPC combat goes by). Asked of on behalf of one NPC, about fire friendly to
///         it (<see cref="NpcFireSide.Friendly"/>) or not (<see cref="NpcFireSide.Hostile"/>).
/// </summary>
/// <remarks>
///     <para>
///         Lines, not rounds: a shot within <see cref="DefaultFireMemory"/> is taken to be firing still, the way it went.
///             That is what a squadmate in the middle of a burst looks like, and what a hostile sweeping a corridor looks
///             like - a fan of its last <see cref="NpcRecentShooterComponent.ShotHistory"/> shots - to someone deciding
///             whether to cross it.
///     </para>
///     <para>
///         For now, read by <c>DiveOperator</c>: never across a friend's fire, and not across a hostile's if there is a
///             way round. Meant also for whatever reacts to being shot at - being pinned down, say - through
///             <see cref="IsUnderFire"/>.
///     </para>
///     <para>
///         Every recent shooter is walked on each ask, rather than kept in a spatial index: shooters are few, the time
///             test is first and costs a field read, and nothing asks every tick. A shot's reach is a ray against anchored
///             things only - mobs share their layers with walls, and a miss flies on past them - cast the first time anyone
///             asks about that shot and kept (<see cref="NpcRecentShot.Reach"/>).
///     </para>
/// </remarks>
public sealed partial class NpcGunfireSystem
{
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;
    [Dependency] private NpcLineOfSightSystem _npcLineOfSightSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    /// <summary>
    ///     How recently a gun must have fired for its line of fire to count.
    /// </summary>
    public static readonly TimeSpan DefaultFireMemory = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How far a line of fire goes with nothing in the way, in tiles: as far as a miss is worth worrying about.
    /// </summary>
    public const float MaxFireReach = 32f;

    /// <summary>
    ///     How close to a line of fire counts as in it, beyond the body asked about: the gun's spread, at range.
    /// </summary>
    public const float DefaultFireMargin = 0.5f;

    /// <summary>
    ///     Whether going from <paramref name="from"/> to <paramref name="to"/> would come within <paramref name="clearance"/> of
    ///         the line of fire of anyone on <paramref name="sides"/> as seen by <paramref name="uid"/>, which has fired within
    ///         <paramref name="memory"/> (<see cref="DefaultFireMemory"/> if null). <paramref name="uid"/>'s own fire never
    ///         counts.
    /// </summary>
    public bool CrossesFire(EntityUid uid,
        MapCoordinates from,
        MapCoordinates to,
        NpcFireSide sides,
        float clearance,
        TimeSpan? memory = null)
    {
        if (from.MapId != to.MapId || sides == NpcFireSide.None)
            return false;

        var since = _gameTiming.CurTime - (memory ?? DefaultFireMemory);

        var enumerator = EntityQueryEnumerator<NpcRecentShooterComponent>();
        while (enumerator.MoveNext(out var shooterUid, out var recentShooterComponent))
        {
            // Cheapest first: most who ever fired have not lately.
            if (recentShooterComponent.LastShotAt < since || shooterUid == uid)
                continue;

            NpcFireSide? side = null;

            for (var i = 0; i < NpcRecentShooterComponent.ShotHistory; i++)
            {
                ref var shot = ref recentShooterComponent.Shots[i];
                if (shot.At < since ||
                    !TryGetLineOfFire(ref shot, out var fireFrom, out var fireTo) ||
                    fireFrom.MapId != from.MapId ||
                    NpcCoordinates.GetSegmentDistance(from.Position, to.Position, fireFrom.Position, fireTo.Position) > clearance)
                    continue;

                side ??= _npcFactionSystem.IsEntityFriendly(uid, shooterUid) ? NpcFireSide.Friendly : NpcFireSide.Hostile;
                if ((sides & side.Value) != 0x0)
                    return true;

                // The same shooter is on the same side for every shot.
                break;
            }
        }

        return false;
    }

    /// <summary>
    ///     Whether <paramref name="uid"/>, where it stands, is within <paramref name="clearance"/> of the line of fire of anyone
    ///         on <paramref name="sides"/> that has fired within <paramref name="memory"/>: being shot at, or caught in fire
    ///         meant for someone else.
    /// </summary>
    public bool IsUnderFire(EntityUid uid, NpcFireSide sides, float clearance, TimeSpan? memory = null)
    {
        var coordinates = _transformSystem.GetMapCoordinates(uid);
        return CrossesFire(uid, coordinates, coordinates, sides, clearance, memory);
    }

    /// <summary>
    ///     <paramref name="shot"/>'s line of fire: from where it was fired, along where it was aimed, to the first anchored
    ///         thing its round collides with, or <see cref="MaxFireReach"/>. False if it has none to speak of: its grid gone,
    ///         or aimed at where it was fired from.
    /// </summary>
    private bool TryGetLineOfFire(ref NpcRecentShot shot, out MapCoordinates from, out MapCoordinates to)
    {
        from = to = MapCoordinates.Nullspace;

        if (TerminatingOrDeleted(shot.From.EntityId) || TerminatingOrDeleted(shot.To.EntityId))
            return false;

        from = _transformSystem.ToMapCoordinates(shot.From);
        var aim = _transformSystem.ToMapCoordinates(shot.To);
        var offset = aim.Position - from.Position;

        if (aim.MapId != from.MapId || offset.LengthSquared() < 0.0001f)
            return false;

        var direction = Vector2.Normalize(offset);

        if (shot.Reach < 0f)
            shot.Reach = _npcLineOfSightSystem.GetClearDistance(from, direction, MaxFireReach, (int) shot.Mask, staticOnly: true);

        to = from.Offset(direction * shot.Reach);
        return true;
    }
}

/// <summary>
///     Whose fire, as seen by the one asking: see <see cref="NpcGunfireSystem.CrossesFire"/>.
/// </summary>
[Flags]
public enum NpcFireSide : byte
{
    None = 0,

    /// <summary>
    ///     From anyone friendly to the one asking.
    /// </summary>
    Friendly = 1 << 0,

    /// <summary>
    ///     From anyone else: hostile, or neutral, whose rounds hurt just the same.
    /// </summary>
    Hostile = 1 << 1,

    Any = Friendly | Hostile,
}
