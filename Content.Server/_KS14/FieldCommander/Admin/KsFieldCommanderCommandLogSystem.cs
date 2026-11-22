using Content.Server.Administration.Logs;
using Content.Shared.Database;
using Robust.Shared.Console;

namespace Content.Server._KS14.FieldCommander.Admin;

/// <summary>
///     Writes every field commander console command (<c>ks_fc*</c>) to the admin log, with who ran it, as the admin
///         window's changes are. Done here, off the console host's own event, so the commands themselves stay as
///         they are.
/// </summary>
public sealed partial class KsFieldCommanderCommandLogSystem : EntitySystem
{
    public const string CommandPrefix = "ks_fc";

    [Dependency] private IAdminLogManager _adminLogManager = default!;
    [Dependency] private IConsoleHost _consoleHost = default!;

    public override void Initialize()
    {
        base.Initialize();
        // The console host outlives the system: unsubscribed in Shutdown.
        _consoleHost.AnyCommandExecuted += OnAnyCommandExecuted;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _consoleHost.AnyCommandExecuted -= OnAnyCommandExecuted;
    }

    /// <remarks>
    ///     Raised once the shell is known to be allowed the command, just before it runs.
    /// </remarks>
    private void OnAnyCommandExecuted(IConsoleShell shell, string commandName, string argStr, string[] args)
    {
        if (!commandName.StartsWith(CommandPrefix, StringComparison.Ordinal))
            return;

        if (shell.Player is { } player)
            _adminLogManager.Add(LogType.AdminCommands, LogImpact.Medium, $"{player:player} ran the field commander command \"{argStr}\"");
        else
            _adminLogManager.Add(LogType.AdminCommands, LogImpact.Medium, $"The server console ran the field commander command \"{argStr}\"");
    }
}
