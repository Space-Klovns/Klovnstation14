using Content.Shared._KS14.Hierarchy; // KS14
using Content.Shared._KS14.Klovnmed; // KS14
using Content.Shared.FixedPoint; // KS14
using Robust.Shared.Containers;
using Robust.Shared.GameStates;

namespace Content.Shared.Body;

/// <summary>
/// Component on the entity that "has" a body, and that oversees entities with the <see cref="OrganComponent"/> inside it.
/// </summary>
/// <seealso cref="BodySystem" />
/// <seealso cref="SharedVisualBodySystem" />
[RegisterComponent, NetworkedComponent]
[Access(typeof(BodySystem), typeof(BodyHierarchySystem) /* KS14: Klovnmed access */)]
public sealed partial class BodyComponent : Component
{
    // KS14: No just no
    //public const string ContainerID = "body_organs";

    // KS14: Removed, you need to use hierarchy for it
    // /// <summary>
    // /// The actual container with entities with <see cref="OrganComponent" /> in it
    // /// </summary>
    // [ViewVariables]
    // public Container? Organs;
}

/// <summary>
/// Raised on organ entity, when it is inserted into a body
/// </summary>
[ByRefEvent]
public readonly record struct OrganGotInsertedEvent(EntityUid Target, BodyComponent BodyComponent, OrganComponent OrganComponent); // KS14: Added BodyComponent and OrganComponent

/// <summary>
/// Raised on organ entity, when it is removed from a body
/// </summary>
[ByRefEvent]
public readonly record struct OrganGotRemovedEvent(EntityUid Target, BodyComponent BodyComponent, OrganComponent OrganComponent); // KS14: Added BodyComponent and OrganComponent

/// <summary>
/// Raised on body entity, when an organ is inserted into it
/// </summary>
[ByRefEvent]
public readonly record struct OrganInsertedIntoEvent(EntityUid Organ, BodyComponent BodyComponent, OrganComponent OrganComponent); // KS14: Added BodyComponent and OrganComponent

/// <summary>
/// Raised on body entity, when an organ is removed from it
/// </summary>
[ByRefEvent]
public readonly record struct OrganRemovedFromEvent(EntityUid Organ, BodyComponent BodyComponent, OrganComponent OrganComponent); // KS14: Added BodyComponent and OrganComponent
