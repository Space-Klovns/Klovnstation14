using Content.Server.Administration;
using Content.Server.NPC.HTN;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Toggles the hunt debug overlay for the invoking player - rooms, the ways into them, routes round them, what has
///         been searched and what has not, and every member's order - for every hunt, or one squad's, given a squad
///         or any NPC in it. See <see cref="NpcHuntDebugSystem"/>.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class HuntDebugCommand : LocalizedEntityCommands
{
    [Dependency] private NpcHuntDebugSystem _npcHuntDebugSystem = default!;

    public override string Command => "ks_huntdebug";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("cmd-ks_huntdebug-no-session"));
            return;
        }

        if (args.Length > 1)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        EntityUid? target = null;

        if (args.Length == 1)
        {
            if (!NetEntity.TryParse(args[0], out var netTarget) || !EntityManager.TryGetEntity(netTarget, out var targetUid))
            {
                shell.WriteError(Loc.GetString("cmd-ks_huntdebug-invalid-entity", ("entity", args[0])));
                return;
            }

            target = targetUid;
        }

        var (enabled, resultTarget) = _npcHuntDebugSystem.Toggle(player, target);

        if (!enabled)
        {
            shell.WriteLine(Loc.GetString("cmd-ks_huntdebug-disabled"));
            return;
        }

        shell.WriteLine(resultTarget is { } trackedUid
            ? Loc.GetString("cmd-ks_huntdebug-enabled-single", ("entity", EntityManager.ToPrettyString(trackedUid)))
            : Loc.GetString("cmd-ks_huntdebug-enabled-all"));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        return CompletionResult.FromHintOptions(
            CompletionHelper.Components<HTNComponent>(args[0], EntityManager),
            Loc.GetString("cmd-ks_huntdebug-entity-hint"));
    }
}
