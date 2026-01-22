namespace Content.Shared._Starlight.Plumbing.Components;

/// <summary>
///     Marker component for plumbing machines that can be plunged.
///     When a plunger is used on this entity, all solution containers are emptied onto the floor.
/// </summary>
[RegisterComponent]
public sealed partial class PlungeableComponent : Component;
