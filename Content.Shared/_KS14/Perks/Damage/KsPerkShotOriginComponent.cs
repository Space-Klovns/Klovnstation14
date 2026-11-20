using System.Numerics;
using Robust.Shared.Map;

namespace Content.Shared._KS14.Perks.Damage;

/// <summary>
///     Where a projectile was fired from and by whom, stamped at fire time for abilities that scale damage with the
///         distance flown. On hit, <see cref="KsModifyPerkShotRangeDamageEvent"/> is raised on the shooter.
/// </summary>
[RegisterComponent, Access(typeof(KsPerkGunSystem))]
public sealed partial class KsPerkShotOriginComponent : Component
{
    [ViewVariables]
    public EntityUid Shooter;

    [ViewVariables]
    public MapId OriginMap = MapId.Nullspace;

    [ViewVariables]
    public Vector2 OriginPosition;
}

/// <summary>
///     Raised on the shooter when one of their shots hits something <see cref="Distance"/> tiles from where it was
///         fired: what its damage is multiplied by. Pure: handlers only multiply <see cref="Multiplier"/>.
/// </summary>
[ByRefEvent]
public record struct KsModifyPerkShotRangeDamageEvent(float Distance, float Multiplier);
