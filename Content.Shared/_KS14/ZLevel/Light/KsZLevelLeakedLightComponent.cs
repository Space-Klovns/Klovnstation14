namespace Content.Shared._KS14.ZLevel.Light;

/// <summary>
///     Marks a light as a stand-in for a real one on a z-level above it, rather than a light of its own.
/// </summary>
/// <remarks>
///     Shared only because it is named by a prototype, which both sides load. Nothing ever carries it but a
///         client-side entity spawned by the client's own leak system, and nothing but that system reads it -
///         it is what stops a leak from leaking again, one z-level at a time, all the way down the stack.
/// </remarks>
[RegisterComponent]
public sealed partial class KsZLevelLeakedLightComponent : Component;
