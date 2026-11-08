namespace Content.Shared._KS14.GhostRoleNotice;

/// <summary>
///     Marks a ghost role as unimportant: ghosts are not told when it appears.
///         Roles with an antagonist mind role are announced regardless.
/// </summary>
[RegisterComponent]
public sealed partial class KsGhostRoleQuietComponent : Component;
