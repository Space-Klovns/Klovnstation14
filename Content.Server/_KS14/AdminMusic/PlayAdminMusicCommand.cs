using System.Linq;
using Content.Server.Administration;
using Content.Shared._KS14.AdminMusic;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._KS14.AdminMusic;

[AdminCommand(AdminFlags.Fun)]
public sealed class PlayAdminMusicCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IResourceManager _resourceManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly KsAdminMusicManager _adminMusicManager = default!;

    public override string Command => "playadminmusic";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var volume = 1f;

        if (args.Length == 2)
        {
            if (!float.TryParse(args[1].Trim('%'), out var volumePercentage) ||
                volumePercentage < 0f ||
                volumePercentage > 100f)
            {
                shell.WriteError(Loc.GetString("cmd-playadminmusic-invalid-volume"));
                return;
            }

            volume = volumePercentage * 0.01f;
        }
        else if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("cmd-playadminmusic-invalid-args"));
            return;
        }

        if (!ResPath.IsValidPath(args[0]))
        {
            shell.WriteError(Loc.GetString("cmd-playadminmusic-invalid-path"));
            return;
        }

        var path = new ResPath(args[0]);
        if (!_resourceManager.ContentFileExists(path))
        {
            shell.WriteError(Loc.GetString("cmd-playadminmusic-bad-path"));
            return;
        }

        if (_adminMusicManager.ActiveEntries.Any((otherEntry) => otherEntry.SoundPath == path))
        {
            shell.WriteError(Loc.GetString("cmd-playadminmusic-fuckoff"));
            return;
        }

        var entry = new KsAdminMusicEntry(path, volume, _gameTiming.CurTime);
        _adminMusicManager.AddEntry(entry);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(CompletionHelper.AudioFilePath(args[0], _prototypeManager, _resourceManager), Loc.GetString("cmd-playadminmusic-path-completion")),
            2 => CompletionResult.FromHint(Loc.GetString("cmd-playadminmusic-volume-completion")),
            _ => CompletionResult.Empty
        };
    }
}
