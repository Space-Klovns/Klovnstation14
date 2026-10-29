using System.Diagnostics.CodeAnalysis;
using Content.Server.Administration;
using Content.Server.Nuke;
using Content.Server.Station.Systems;
using Content.Shared._KS14.Nuke;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._KS14.Nuke;

/// <summary>
///     Detonates a nuke on the spot, playing the station explosion cinematic for everyone in game.
///     Without an ending it picks one from the nuke's situation, and without a nuke it picks
///     the first unexploded nuke that sits on a station.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class KsNukeDetonateCommand : LocalizedEntityCommands
{
    [Dependency] private NukeSystem _nuke = default!;
    [Dependency] private StationSystem _station = default!;

    public override string Command => "nukedetonate";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 2)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        KsNukeExplosionEnding? ending = null;
        if (args.Length >= 1)
        {
            if (!Enum.TryParse<KsNukeExplosionEnding>(args[0], true, out var parsedEnding) ||
                !Enum.IsDefined(parsedEnding))
            {
                shell.WriteError(Loc.GetString("cmd-nukedetonate-invalid-ending", ("ending", args[0])));
                return;
            }

            ending = parsedEnding;
        }

        Entity<NukeComponent>? nuke;
        if (args.Length == 2)
        {
            if (!EntityManager.TryParseNetEntity(args[1], out var parsed) ||
                !EntityManager.TryGetComponent(parsed, out NukeComponent? nukeComp))
            {
                shell.WriteError(Loc.GetString("cmd-nukedetonate-invalid-nuke", ("nuke", args[1])));
                return;
            }

            nuke = (parsed.Value, nukeComp);
        }
        else if (!TryFindStationNuke(out nuke))
        {
            shell.WriteError(Loc.GetString("cmd-nukedetonate-not-found"));
            return;
        }

        if (nuke.Value.Comp.Exploded)
        {
            shell.WriteError(Loc.GetString("cmd-nukedetonate-already-exploded"));
            return;
        }

        _nuke.KsActivateBomb(nuke.Value.Owner, ending);
    }

    private bool TryFindStationNuke([NotNullWhen(true)] out Entity<NukeComponent>? nuke)
    {
        var query = EntityManager.EntityQueryEnumerator<NukeComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var nukeComp, out var xform))
        {
            if (nukeComp.Exploded || _station.GetOwningStation(uid, xform) == null)
                continue;

            nuke = (uid, nukeComp);
            return true;
        }

        nuke = null;
        return false;
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(Enum.GetNames<KsNukeExplosionEnding>(),
                Loc.GetString("cmd-nukedetonate-ending-completion")),
            2 => CompletionResult.FromHintOptions(CompletionHelper.Components<NukeComponent>(args[1]),
                Loc.GetString("cmd-nukedetonate-nuke-completion")),
            _ => CompletionResult.Empty,
        };
    }
}
