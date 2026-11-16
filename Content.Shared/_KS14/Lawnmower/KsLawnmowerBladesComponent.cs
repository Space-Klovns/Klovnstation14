using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Lawnmower;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(SharedKsLawnmowerBladesSystem))]
public sealed partial class KsLawnmowerBladesComponent : Component
{
    /// <summary>
    ///     Grass floor types that the blades replace with mowed turf.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<ContentTileDefinition>> MowableTiles = new();

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

    [ViewVariables, AutoNetworkedField, AutoPausedField]
    public TimeSpan NextDriveSound;

    /// <summary>
    ///     Targets the blades already hit and still touch. Leaving the blades re-arms a hit.
    /// </summary>
    /// <remarks>
    ///     Net entities, as a gibbed target is deleted while still in here and can't be networked as a uid.
    /// </remarks>
    [ViewVariables, AutoNetworkedField]
    public HashSet<NetEntity> HitTargets = new();

    // Only the server mows and leaves tracks.
    public (EntityUid Grid, Vector2i Indices)? LastTile;
    public int BloodyTiles;
}
