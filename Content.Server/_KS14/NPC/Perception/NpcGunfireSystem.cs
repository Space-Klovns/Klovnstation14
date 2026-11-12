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

    [Dependency] private EntityQuery<NpcRecentShooterComponent> _recentShooterQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunShotEvent>(OnGunShot);
    }

    private void OnGunShot(ref GunShotEvent args)
    {
        EnsureComp<NpcRecentShooterComponent>(args.User).LastShotAt = _gameTiming.CurTime;
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
