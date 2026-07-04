using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._KS14.AdminMusic;

[AdminCommand(AdminFlags.Fun)]
public sealed class StopAllAdminMusicCommand : LocalizedEntityCommands
{
    [Dependency] private readonly KsAdminMusicManager _adminMusicManager = default!;

    public override string Command => "stopalladminmusic";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _adminMusicManager.RemoveAllEntries();
    }
}
