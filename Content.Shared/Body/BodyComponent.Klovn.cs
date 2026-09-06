using Content.Shared._KS14.Hierarchy; // KS14
using Content.Shared._KS14.Klovnmed; // KS14
using Content.Shared.FixedPoint; // KS14
using Robust.Shared.Containers;

namespace Content.Shared.Body;

public sealed partial class BodyComponent : IHierarchyComponent
{
    /// <summary>
    ///     Organ categories present and their entities.
    ///         Only one is allowed.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    [Access(typeof(BodyHierarchySystem), Other = AccessPermissions.ReadExecute)]
    public Dictionary<Robust.Shared.Prototypes.ProtoId<OrganCategoryPrototype>, Entity<OrganComponent>> PresentOrganCategories = [];

    [ViewVariables(VVAccess.ReadOnly)]
    public List<EntityUid> RecursiveChildUids { get; set; }

    [ViewVariables(VVAccess.ReadOnly)]
    public Container Container { get; set; }

    // KS14: No just no
    //public const string ContainerID = "body_organs";

    // KS14: Removed, you need to use hierarchy for it
    // /// <summary>
    // /// The actual container with entities with <see cref="OrganComponent" /> in it
    // /// </summary>
    // [ViewVariables]
    // public Container? Organs;

    /// <summary>
    ///     Amount of damage taken in one hit (currently explosions only)
    ///         to dismember SOMETHING.
    /// </summary>
    [DataField]
    public FixedPoint2 DismembermentThreshold = FixedPoint2.New(80f);
}
