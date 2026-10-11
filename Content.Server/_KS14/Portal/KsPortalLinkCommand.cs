using System.Diagnostics.CodeAnalysis;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared._KS14.Portal;
using Content.Shared.Teleportation.Components;
using Robust.Shared.Console;

namespace Content.Server._KS14.Portal;

/// <summary>
///     Links two portals to each other, replacing any links either had.
/// </summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed partial class KsPortalLinkCommand : LocalizedEntityCommands
{
    [Dependency] private KsPortalLinkSystem _portalLinkSystem = default!;

    public override string Command => "ksportallink";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Loc.GetString("cmd-ksportallink-invalid-args"));
            return;
        }

        if (!TryParsePortal(shell, args[0], out var firstUid) || !TryParsePortal(shell, args[1], out var secondUid))
            return;

        if (firstUid == secondUid)
        {
            shell.WriteError(Loc.GetString("cmd-ksportallink-same-entity"));
            return;
        }

        if (!_portalLinkSystem.TryLinkExclusive(firstUid.Value, secondUid.Value))
        {
            shell.WriteError(Loc.GetString("cmd-ksportallink-failed"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-ksportallink-success",
            ("first", EntityManager.ToPrettyString(firstUid.Value)),
            ("second", EntityManager.ToPrettyString(secondUid.Value))));
    }

    private bool TryParsePortal(IConsoleShell shell, string argument, [NotNullWhen(true)] out EntityUid? uid)
    {
        uid = null;

        if (!NetEntity.TryParse(argument, out var netEntity) || !EntityManager.TryGetEntity(netEntity, out var resolved))
        {
            shell.WriteError(Loc.GetString("cmd-ksportallink-bad-uid", ("value", argument)));
            return false;
        }

        if (!EntityManager.HasComponent<KsPortalViewComponent>(resolved.Value))
        {
            shell.WriteError(Loc.GetString("cmd-ksportallink-not-a-portal",
                ("entity", EntityManager.ToPrettyString(resolved.Value))));
            return false;
        }

        uid = resolved.Value;
        return true;
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(
                CompletionHelper.Components<KsPortalViewComponent>(args[0], EntityManager),
                Loc.GetString("cmd-ksportallink-first-completion")),
            2 => CompletionResult.FromHintOptions(
                CompletionHelper.Components<KsPortalViewComponent>(args[1], EntityManager),
                Loc.GetString("cmd-ksportallink-second-completion")),
            _ => CompletionResult.Empty,
        };
    }
}
