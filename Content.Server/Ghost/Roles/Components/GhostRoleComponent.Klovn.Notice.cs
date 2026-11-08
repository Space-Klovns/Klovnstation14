namespace Content.Server.Ghost.Roles.Components;

// Partial so the notice flag and the role's raw name key can live outside the upstream file.
public sealed partial class GhostRoleComponent
{
    /// <summary>
    ///     The role name as written: a locale key or plain text, not yet translated.
    /// </summary>
    public string RoleNameKey => _roleName;
}
