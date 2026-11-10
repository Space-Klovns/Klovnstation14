namespace Content.Shared._KS14.GhostRoleNotice;

/// <summary>
///     Marks a ghost role worth telling ghosts about: when it appears mid-round, every ghost hears a sound and gets a
///         chat line. Ghost roles without it appear silently. Admin-made roles get it from the command that makes them.
/// </summary>
[RegisterComponent]
public sealed partial class KsGhostRoleNotableComponent : Component;
