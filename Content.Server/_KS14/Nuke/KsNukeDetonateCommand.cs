using System.Diagnostics.CodeAnalysis;
using Content.Server.Administration;
using Content.Server.Nuke;
using Content.Server.Station.Systems;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._KS14.Nuke;

/// <summary>
///     Detonates a nuke on the spot, playing the station explosion cinematic for everyone in game.
///     Without an argument it picks the first unexploded nuke that sits on a station.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class KsNukeDetonateCommand : LocalizedEntityCommands
{
    [Dependency] private NukeSystem _nuke = default!;
    [Dependency] private StationSystem _station = default!;

    public override string Command => "nukedetonate";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 1)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        EntityUid nukeUid;
        NukeComponent? nuke;
        if (args.Length == 1)
        {
            if (!EntityManager.TryParseNetEntity(args[0], out var parsed) ||
                !EntityManager.TryGetComponent(parsed, out nuke))
            {
                shell.WriteError(Loc.GetString("cmd-nukedetonate-invalid-nuke", ("nuke", args[0])));
                return;
            }

            nukeUid = parsed.Value;
        }
        else if (!TryFindStationNuke(out nukeUid, out nuke))
        {
            shell.WriteError(Loc.GetString("cmd-nukedetonate-not-found"));
            return;
        }

        if (nuke.Exploded)
        {
            shell.WriteError(Loc.GetString("cmd-nukedetonate-already-exploded"));
            return;
        }

        _nuke.ActivateBomb(nukeUid, nuke);
    }

    private bool TryFindStationNuke(out EntityUid uid, [NotNullWhen(true)] out NukeComponent? nuke)
    {
        var query = EntityManager.EntityQueryEnumerator<NukeComponent, TransformComponent>();
        while (query.MoveNext(out uid, out nuke, out var xform))
        {
            if (!nuke.Exploded && _station.GetOwningStation(uid, xform) != null)
                return true;
        }

        uid = default;
        nuke = null;
        return false;
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(CompletionHelper.Components<NukeComponent>(args[0]),
                Loc.GetString("cmd-nukedetonate-nuke-completion"));
        }

        return CompletionResult.Empty;
    }
}
