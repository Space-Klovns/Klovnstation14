using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared._KS14.Portal;
using Robust.Shared.Console;

namespace Content.Server._KS14.Portal;

/// <summary>
///     Removes every link from a portal.
/// </summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed partial class KsPortalUnlinkCommand : LocalizedEntityCommands
{
    [Dependency] private KsPortalLinkSystem _portalLinkSystem = default!;

    public override string Command => "ksportalunlink";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("cmd-ksportalunlink-invalid-args"));
            return;
        }

        if (!NetEntity.TryParse(args[0], out var netEntity) || !EntityManager.TryGetEntity(netEntity, out var uid))
        {
            shell.WriteError(Loc.GetString("cmd-ksportallink-bad-uid", ("value", args[0])));
            return;
        }

        if (!EntityManager.HasComponent<KsPortalViewComponent>(uid.Value))
        {
            shell.WriteError(Loc.GetString("cmd-ksportallink-not-a-portal",
                ("entity", EntityManager.ToPrettyString(uid.Value))));
            return;
        }

        _portalLinkSystem.UnlinkAll(uid.Value);
        shell.WriteLine(Loc.GetString("cmd-ksportalunlink-success", ("entity", EntityManager.ToPrettyString(uid.Value))));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(
                CompletionHelper.Components<KsPortalViewComponent>(args[0], EntityManager),
                Loc.GetString("cmd-ksportallink-first-completion"))
            : CompletionResult.Empty;
    }
}
