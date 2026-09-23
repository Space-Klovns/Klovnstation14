using Content.Server.Administration;
using Content.Shared._KS14.ZLevel;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.ZLevel;

[AdminCommand(AdminFlags.Debug)]
public sealed partial class KsZLevelAddCommand : LocalizedEntityCommands
{
    [Dependency] private KsZLevelSystem _zLevelSystem = default!;

    public override string Command => "zlevel_add";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Loc.GetString("cmd-zlevel_add-invalid-args"));
            return;
        }

        if (!EntityUid.TryParse(args[0], out var targetUid) ||
            !targetUid.IsValid() ||
            !EntityManager.EntityExists(targetUid) ||
            !EntityManager.HasComponent<MapComponent>(targetUid))
        {
            goto badUid;
        }

        if (!EntityUid.TryParse(args[1], out var addedUid) ||
            !addedUid.IsValid() ||
            !EntityManager.EntityExists(addedUid) ||
            !EntityManager.HasComponent<MapComponent>(targetUid))
        {
            goto badUid;
        }

        _zLevelSystem.AddZLevelDirectlyAbove(
            (targetUid, EntityManager.EnsureComponent<KsZLevelComponent>(targetUid)),
            (addedUid, EntityManager.EnsureComponent<KsZLevelComponent>(addedUid))
        );
        return;
    badUid:
        shell.WriteError(Loc.GetString("cmd-zlevel_add-invalid-uid"));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1 &&
            args.Length != 2)
            return CompletionResult.Empty;

        return CompletionResult.FromHintOptions(
            CompletionHelper.MapUids(EntityManager),
            Loc.GetString("cmd-zlevel_add-completion"));
    }
}
