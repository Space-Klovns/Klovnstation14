using System.Numerics;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.WeldedParts;

/// <summary>
///     Spawns other entities at map init and welds each to this one with a physics weld joint, so they move as one: a tank
///         and its turret. They copy this one's NPC factions, go when it goes, and take it with them when one of them is
///         destroyed. Only this one is the thing itself (what a spawner counts, what is paid for); the parts are parts.
/// </summary>
[RegisterComponent, Access(typeof(KsWeldedPartsSystem))]
public sealed partial class KsWeldedPartsComponent : Component
{
    [DataField(required: true)]
    public List<KsWeldedPart> Parts = new();

    /// <summary>
    ///     The parts spawned, while they last.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> Spawned = new();
}

[DataDefinition]
public sealed partial class KsWeldedPart
{
    [DataField(required: true)]
    public EntProtoId Prototype;

    /// <summary>
    ///     Where it sits, relative to the owner and turned with it.
    /// </summary>
    [DataField]
    public Vector2 Offset;

    /// <summary>
    ///     How stiffly the weld holds the part's turn against the owner's: zero holds it rigid; a little lets a turret turn
    ///         on its own while the hull keeps it in place.
    /// </summary>
    [DataField]
    public float AngularStiffness;

    [DataField]
    public float AngularDamping;

    /// <summary>
    ///     Whether the part takes the owner's NPC factions, for a turret to shoot at whatever the hull is hostile to.
    /// </summary>
    [DataField]
    public bool CopyFactions = true;
}

/// <summary>
///     A part spawned by <see cref="KsWeldedPartsComponent"/>, and what it belongs to.
/// </summary>
[RegisterComponent, Access(typeof(KsWeldedPartsSystem))]
public sealed partial class KsWeldedPartComponent : Component
{
    [ViewVariables]
    public EntityUid Root;
}
