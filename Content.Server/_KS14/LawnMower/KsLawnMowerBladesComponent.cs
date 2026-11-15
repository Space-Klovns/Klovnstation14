using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;

namespace Content.Server._KS14.LawnMower;

[RegisterComponent, Access(typeof(KsLawnMowerSystem))]
public sealed partial class KsLawnMowerBladesComponent : Component
{
    [DataField(required: true)]
    public SoundSpecifier DriveSound = default!;

    [DataField(required: true)]
    public SoundSpecifier BumpSound = default!;

    [DataField(required: true)]
    public SoundSpecifier BladeSound = default!;

    [DataField(required: true)]
    public SoundSpecifier CrushSound = default!;

    [DataField(required: true)]
    public DamageSpecifier BumpDamage = new();

    [DataField(required: true)]
    public FixedPoint2 CrushDamage;

    public TimeSpan NextDriveSound;
    public (EntityUid Grid, Vector2i Indices)? LastTile;
    public int BloodyTiles;

    // A target can have several fixtures. Count contacts so only the final exit re-arms a hit.
    public readonly Dictionary<EntityUid, int> Contacts = new();
    public readonly HashSet<EntityUid> HitTargets = new();
}
