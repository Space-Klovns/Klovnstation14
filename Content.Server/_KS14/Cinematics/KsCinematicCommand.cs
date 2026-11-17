using Content.Server.Administration;
using Content.Shared._KS14.Cinematics;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Cinematics;

/// <summary>
///     Plays any cinematic to everyone in game.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class KsCinematicCommand : LocalizedEntityCommands
{
    [Dependency] private KsCinematicSystem _cinematicSystem = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    public override string Command => "cinematic";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!_prototypeManager.TryIndex<KsCinematicPrototype>(args[0], out var cinematic))
        {
            shell.WriteError(Loc.GetString("cmd-cinematic-invalid", ("cinematic", args[0])));
            return;
        }

        _cinematicSystem.Play(cinematic.ID);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(CompletionHelper.PrototypeIDs<KsCinematicPrototype>(proto: _prototypeManager),
                Loc.GetString("cmd-cinematic-completion")),
            _ => CompletionResult.Empty,
        };
    }
}
