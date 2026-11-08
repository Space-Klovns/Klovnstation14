namespace Content.Server.Ghost.Roles.Components;

// Partial so the notice flag and the role's raw name key can live outside the upstream file.
public sealed partial class GhostRoleComponent
{
    /// <summary>
    ///     Tells every ghost when this role appears mid-round, even if none of its mind roles is an antagonist.
    ///         Antagonist ghost roles always do; roles that exist at round start never do.
    /// </summary>
    [DataField]
    public bool Notable;

    /// <summary>
    ///     The role name as written: a locale key or plain text, not yet translated.
    /// </summary>
    public string RoleNameKey => _roleName;
}
