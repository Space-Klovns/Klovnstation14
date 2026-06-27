using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.GameTicking;
using Robust.Shared.Console;

namespace Content.Server.GameTicking.Commands
{
    [AnyCommand]
    sealed class ObserveCommand : IConsoleCommand
    {
        [Dependency] private readonly IEntityManager _e = default!;
        [Dependency] private readonly IAdminManager _adminManager = default!;

        public string Command => "observe";
        public string Description => "";
        public string Help => "";

        public void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (shell.Player is not { } player)
            {
                shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
                return;
            }

            var ticker = _e.System<GameTicker>();
            var isAdminCommand = args.Length > 0 && args[0].ToLower() == "admin"; // KS14: Moved here
            var isAdmin = _adminManager.IsAdmin(player); // KS14 addition

            if (ticker.RunLevel == GameRunLevel.PreRoundLobby &&
                !(isAdminCommand && isAdmin) /* KS14: Admins may observe before the game starts */)
            {
                shell.WriteError("Wait until the round starts.");
                return;
            }


            if (!isAdminCommand && isAdmin /* KS14: Use isAdmin instead of re-evaluating*/)
            {
                _adminManager.DeAdmin(player);
            }

            if (ticker.PlayerGameStatuses.TryGetValue(player.UserId, out var status) &&
                status != PlayerGameStatus.JoinedGame)
            {
                ticker.JoinAsObserver(player);
            }
            else
            {
                shell.WriteError($"{player.Name} is not in the lobby.   This incident will be reported.");
            }
        }
    }
}
