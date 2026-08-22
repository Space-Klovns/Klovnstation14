using Content.Server.Administration;
using Content.Server.NPC.HTN;
using Content.Server._KS14.NPC.Systems;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._KS14.NPC.Commands;

/// <summary>
/// Toggles the tactical position debug overlay (candidate scores, chosen spot, live claims) for the
/// invoking player - either for every NPC using the query, or scoped to a single entity given by argument.
/// See <see cref="NpcTacticalPositionDebugSystem"/>.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class TacticalPositionDebugCommand : LocalizedEntityCommands
{
    [Dependency] private NpcTacticalPositionDebugSystem _npcTacticalPositionDebugSystem = default!;

    public override string Command => "ks_tacticalposdebug";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("cmd-ks_tacticalposdebug-no-session"));
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
                shell.WriteError(Loc.GetString("cmd-ks_tacticalposdebug-invalid-entity", ("entity", args[0])));
                return;
            }

            target = targetUid;
        }

        var (enabled, resultTarget) = _npcTacticalPositionDebugSystem.Toggle(player, target);

        if (!enabled)
        {
            shell.WriteLine(Loc.GetString("cmd-ks_tacticalposdebug-disabled"));
            return;
        }

        shell.WriteLine(resultTarget is { } trackedUid
            ? Loc.GetString("cmd-ks_tacticalposdebug-enabled-single", ("entity", EntityManager.ToPrettyString(trackedUid)))
            : Loc.GetString("cmd-ks_tacticalposdebug-enabled-all"));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        return CompletionResult.FromHintOptions(
            CompletionHelper.Components<HTNComponent>(args[0], EntityManager),
            Loc.GetString("cmd-ks_tacticalposdebug-entity-hint"));
    }
}
