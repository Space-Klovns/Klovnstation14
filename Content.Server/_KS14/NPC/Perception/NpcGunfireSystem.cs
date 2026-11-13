using Content.Server._KS14.NPC.LineOfFire;
using Content.Shared.Interaction;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     Notes when whoever fires a gun last fired (<see cref="NpcRecentShooterComponent"/>). A shot is heard by anyone near,
///         so an NPC that can see a hostile knows when it went quiet without seeing through anything.
/// </summary>
public sealed partial class NpcGunfireSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private NpcLineOfFireSystem _npcLineOfFireSystem = default!;

    [Dependency] private EntityQuery<NpcRecentShooterComponent> _recentShooterQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunShotEvent>(OnGunShot);
    }

    private void OnGunShot(ref GunShotEvent args)
    {
        var recentShooterComponent = EnsureComp<NpcRecentShooterComponent>(args.User);
        var now = _gameTiming.CurTime;
        recentShooterComponent.LastShotAt = now;

        // What the rounds collide with, as NPC combat works it out (NpcLineOfFireSystem): the next round, which is the
        //      same as this one but for the last in a magazine, when the one before is the best guess there is.
        var previousIndex = (recentShooterComponent.NextShot + NpcRecentShooterComponent.ShotHistory - 1) % NpcRecentShooterComponent.ShotHistory;
        var previous = recentShooterComponent.Shots[previousIndex];
        if (!_npcLineOfFireSystem.TryGetShotMask(args.User, out var mask))
            mask = previous.At != default ? previous.Mask : SharedInteractionSystem.InRangeUnobstructedMask;

        recentShooterComponent.Shots[recentShooterComponent.NextShot] = new NpcRecentShot
        {
            At = now,
            From = args.FromCoordinates,
            To = args.ToCoordinates,
            Mask = mask,
            Reach = -1f,
        };
        recentShooterComponent.NextShot = (recentShooterComponent.NextShot + 1) % NpcRecentShooterComponent.ShotHistory;
    }

    /// <summary>
    ///     When <paramref name="uid"/> last fired, if it ever has.
    /// </summary>
    public bool TryGetLastShot(EntityUid uid, out TimeSpan lastShotAt)
    {
        lastShotAt = default;

        if (!_recentShooterQuery.TryComp(uid, out var recentShooterComponent))
            return false;

        lastShotAt = recentShooterComponent.LastShotAt;
        return true;
    }
}
