namespace Content.Shared._KS14.Portal;

/// <summary>
///     Marks a light as a stand-in for a real one on the far side of a view portal, rather than a light of its
///         own.
/// </summary>
/// <remarks>
///     Shared only because it is named by a prototype, which both sides load. Nothing ever carries it but a
///         client-side entity spawned by the client's portal light transfer, and it is what stops a stand-in
///         from being carried through a portal again - two portals facing each other would otherwise feed
///         each other light forever.
/// </remarks>
[RegisterComponent]
public sealed partial class KsPortalLeakedLightComponent : Component;
