using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    /// Allows creating and using one-level folders on the action bar.
    /// </summary>
    public static readonly CVarDef<bool> ActionFoldersEnabled =
        CVarDef.Create("klovn.actions.folders_enabled", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Saves and restores action bar ordering and folder membership in client player data.
    /// </summary>
    public static readonly CVarDef<bool> ActionLayoutPersistenceEnabled =
        CVarDef.Create("klovn.actions.layout_persistence_enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
