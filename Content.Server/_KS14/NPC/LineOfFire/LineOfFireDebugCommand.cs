using Content.Server.Administration;
using Content.Server.NPC.HTN;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._KS14.NPC.LineOfFire;

/// <summary>
///     Toggles the line of fire debug overlay for the invoking player: how far the shot of each NPC with a gun on a target
///         would get, and what stops it. See <see cref="NpcLineOfFireDebugSystem"/>.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class LineOfFireDebugCommand : LocalizedEntityCommands
{
    [Dependency] private NpcLineOfFireDebugSystem _npcLineOfFireDebugSystem = default!;

    public override string Command => "ks_lineoffiredebug";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("cmd-ks_lineoffiredebug-no-session"));
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
                shell.WriteError(Loc.GetString("cmd-ks_lineoffiredebug-invalid-entity", ("entity", args[0])));
                return;
            }

            target = targetUid;
        }

        var (enabled, resultTarget) = _npcLineOfFireDebugSystem.Toggle(player, target);

        if (!enabled)
        {
            shell.WriteLine(Loc.GetString("cmd-ks_lineoffiredebug-disabled"));
            return;
        }

        shell.WriteLine(resultTarget is { } trackedUid
            ? Loc.GetString("cmd-ks_lineoffiredebug-enabled-single", ("entity", EntityManager.ToPrettyString(trackedUid)))
            : Loc.GetString("cmd-ks_lineoffiredebug-enabled-all"));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        return CompletionResult.FromHintOptions(
            CompletionHelper.Components<HTNComponent>(args[0], EntityManager),
            Loc.GetString("cmd-ks_lineoffiredebug-entity-hint"));
    }
}
