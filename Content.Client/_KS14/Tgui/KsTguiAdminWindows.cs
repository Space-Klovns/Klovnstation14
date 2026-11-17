using Content.Client.Administration.UI;
using Content.Client.Administration.UI.AdminCamera;
using Content.Client.Administration.UI.AdminRemarks;
using Content.Client.Administration.UI.BanList;
using Content.Client.Administration.UI.BanPanel;
using Content.Client.Administration.UI.Bwoink;
using Content.Client.Administration.UI.Logs;
using Content.Client.Administration.UI.ManageSolutions;
using Content.Client.Administration.UI.Notes;
using Content.Client.Administration.UI.PlayerPanel;
using Content.Client.Administration.UI.SetOutfit;
using Content.Client.Administration.UI.SpawnExplosion;
using Content.Client.Administration.UI.Tabs.AdminbusTab;
using Content.Client.Administration.UI.Tabs.AdminTab;
using Content.Client.Administration.UI.Tabs.AtmosTab;
using Content.Client.Administration.UI.Tabs.PanicBunkerTab;
using Content.Client.Decals.UI;
using Content.Client.Fax.AdminUI;
using Content.Client.Silicons.Laws.SiliconLawEditUi;
using Content.Shared._KS14.Tgui;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     Which windows wear tgui's <c>theme="admin"</c>: the ones only an admin is shown, which tg gives that theme (the Admin
///         Fax Panel, Spawn Panel, Law Panel, Admin Verb Panel and so on). Decided by the window's type in one place, so
///         that a window of ours, upstream's or the engine's takes it without being edited, and so that the list can be read
///         and tested. <see cref="KsTguiChromeUIController"/> asks as each window opens.
/// </summary>
/// <remarks>
///     A window a regular player also sees is not here, whatever it is called: <c>DialogWindow</c> (the quick dialog an admin
///         verb opens is the one a prayer and a fax use), <c>SandboxWindow</c> (anyone in a sandbox round) and the like. The
///         player's own ahelp window is a plain <see cref="DefaultWindow"/>, so it is given the theme where it is made
///         (<c>UserAHelpUIHandler</c>), as tg's "Create Adminhelp" has it.
/// </remarks>
public static class KsTguiAdminWindows
{
    /// <summary>
    ///     The admin-only windows, by exact type.
    /// </summary>
    public static readonly IReadOnlySet<Type> WindowTypes = new HashSet<Type>
    {
        // The menu and its tabs' windows.
        typeof(AdminMenuWindow),
        typeof(AdminShuttleWindow),
        typeof(LoadBlueprintsWindow),
        typeof(AddAtmosWindow),
        typeof(AddGasWindow),
        typeof(FillGasWindow),
        typeof(SetTemperatureWindow),
        typeof(PanicBunkerStatusWindow),

        // The admin's side of ahelp, notes, bans and logs.
        typeof(BwoinkWindow),
        typeof(AdminLogsWindow),
        typeof(AdminNotesWindow),
        typeof(NoteEdit),
        typeof(AdminRemarksWindow),
        typeof(BanPanel),
        typeof(BanListWindow),

        // Things an admin does to a player or a round.
        typeof(PlayerPanel),
        typeof(AdminAnnounceWindow),
        typeof(AdminCameraWindow),
        typeof(AdminFaxWindow),
        typeof(SetOutfitMenu),
        typeof(SpawnExplosionWindow),
        typeof(EditSolutionsWindow),
        typeof(AddReagentWindow),
        typeof(SiliconLawUi),

        // Spawning and mapping. The engine's spawn menus are opened by the sandbox as well, but they are the advanced entity
        //      and tile spawn menus of the request, and tg's Spawn Panel has this theme.
        typeof(EntitySpawnWindow),
        typeof(TileSpawnWindow),
        typeof(DecalPlacerWindow),
    };

    /// <summary>
    ///     The windows that are private classes of another, which <c>typeof</c> cannot name. The test resolves each of these, so
    ///         a rename is not silent.
    /// </summary>
    public static readonly IReadOnlyList<string> NestedWindowTypeNames = new[]
    {
        "Content.Client.Administration.UI.PermissionsEui+Menu",
        "Content.Client.Administration.UI.PermissionsEui+EditAdminWindow",
        "Content.Client.Administration.UI.PermissionsEui+EditAdminRankWindow",
    };

    private static readonly IReadOnlySet<string> NestedNames = new HashSet<string>(NestedWindowTypeNames);

    /// <summary>
    ///     Whether this window is one of the admin-only ones.
    /// </summary>
    public static bool IsAdminWindow(Control control)
    {
        var type = control.GetType();
        return WindowTypes.Contains(type) || (type.FullName is { } name && NestedNames.Contains(name));
    }

    /// <summary>
    ///     Gives the window the admin theme if it is an admin window that has no theme of its own.
    /// </summary>
    public static void Apply(Control control)
    {
        if (control.GetKsTheme() == null && IsAdminWindow(control))
            control.SetKsTheme(KsTguiWindowTheme.Admin);
    }
}
