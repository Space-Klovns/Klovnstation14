using Content.Server.Administration;
using Content.Server.NPC.HTN;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Toggles the squad plan debug overlay for the invoking player - what a squad knows, what it means to do about a hostile
///         it cannot get at, and how far each member's shot gets - for every squad, or one, given a squad or any NPC in it.
///         See <see cref="NpcSquadPlanDebugSystem"/>.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class SquadPlanDebugCommand : LocalizedEntityCommands
{
    [Dependency] private NpcSquadPlanDebugSystem _npcSquadPlanDebugSystem = default!;

    public override string Command => "ks_squadplandebug";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("cmd-ks_squadplandebug-no-session"));
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
                shell.WriteError(Loc.GetString("cmd-ks_squadplandebug-invalid-entity", ("entity", args[0])));
                return;
            }

            target = targetUid;
        }

        var (enabled, resultTarget) = _npcSquadPlanDebugSystem.Toggle(player, target);

        if (!enabled)
        {
            shell.WriteLine(Loc.GetString("cmd-ks_squadplandebug-disabled"));
            return;
        }

        shell.WriteLine(resultTarget is { } trackedUid
            ? Loc.GetString("cmd-ks_squadplandebug-enabled-single", ("entity", EntityManager.ToPrettyString(trackedUid)))
            : Loc.GetString("cmd-ks_squadplandebug-enabled-all"));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        return CompletionResult.FromHintOptions(
            CompletionHelper.Components<HTNComponent>(args[0], EntityManager),
            Loc.GetString("cmd-ks_squadplandebug-entity-hint"));
    }
}
