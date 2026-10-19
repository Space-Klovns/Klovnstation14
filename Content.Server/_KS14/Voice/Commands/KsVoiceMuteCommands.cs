using System.Globalization;
using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Voice.Commands;

/// <summary>
///     <c>vcmute &lt;player&gt; [minutes, 0 = rest of round] [reason...]</c>
/// </summary>
[AdminCommand(AdminFlags.Moderator)]
public sealed partial class KsVoiceMuteCommand : LocalizedEntityCommands
{
    /// <summary>
    ///     Longest timed mute, in minutes (a year). Anything longer is what round mutes and bans are for.
    /// </summary>
    private const float MaxMinutes = 525600f;

    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private KsVoiceSystem _voiceSystem = default!;

    public override string Command => "vcmute";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteError(Loc.GetString("cmd-vcmute-invalid-args"));
            return;
        }

        if (!_playerManager.TryGetSessionByUsername(args[0], out var targetSession))
        {
            shell.WriteError(Loc.GetString("cmd-vcmute-no-player", ("player", args[0])));
            return;
        }

        TimeSpan? duration = null;
        if (args.Length >= 2)
        {
            if (!float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) ||
                !float.IsFinite(minutes) ||
                minutes < 0f ||
                minutes > MaxMinutes)
            {
                shell.WriteError(Loc.GetString("cmd-vcmute-invalid-minutes"));
                return;
            }

            if (minutes > 0f)
                duration = TimeSpan.FromMinutes((double)minutes);
        }

        var reason = args.Length >= 3
            ? string.Join(' ', args.Skip(2))
            : Loc.GetString("ks-voice-mute-reason-none");

        _voiceSystem.Mute(targetSession.UserId, duration, reason, shell.Player);
        shell.WriteLine(Loc.GetString("cmd-vcmute-success", ("player", targetSession.Name)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _playerManager), Loc.GetString("cmd-vcmute-player-completion")),
            2 => CompletionResult.FromHint(Loc.GetString("cmd-vcmute-minutes-completion")),
            _ => CompletionResult.FromHint(Loc.GetString("cmd-vcmute-reason-completion")),
        };
    }
}

/// <summary>
///     <c>vcunmute &lt;player&gt;</c>
/// </summary>
[AdminCommand(AdminFlags.Moderator)]
public sealed partial class KsVoiceUnmuteCommand : LocalizedEntityCommands
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private KsVoiceSystem _voiceSystem = default!;

    public override string Command => "vcunmute";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("cmd-vcunmute-invalid-args"));
            return;
        }

        if (!_playerManager.TryGetSessionByUsername(args[0], out var targetSession))
        {
            shell.WriteError(Loc.GetString("cmd-vcmute-no-player", ("player", args[0])));
            return;
        }

        shell.WriteLine(_voiceSystem.Unmute(targetSession.UserId, shell.Player)
            ? Loc.GetString("cmd-vcunmute-success", ("player", targetSession.Name))
            : Loc.GetString("cmd-vcunmute-not-muted", ("player", targetSession.Name)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _playerManager), Loc.GetString("cmd-vcmute-player-completion"))
            : CompletionResult.Empty;
    }
}

/// <summary>
///     <c>vcmutes</c>: lists active voice mutes.
/// </summary>
[AdminCommand(AdminFlags.Moderator)]
public sealed partial class KsVoiceMuteListCommand : LocalizedEntityCommands
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private KsVoiceSystem _voiceSystem = default!;

    private readonly List<(NetUserId UserId, TimeSpan Remaining)> _autoMutes = [];

    public override string Command => "vcmutes";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _voiceSystem.GetAutoMutes(_autoMutes);
        if (_voiceSystem.Mutes.Count == 0 && _autoMutes.Count == 0)
        {
            shell.WriteLine(Loc.GetString("cmd-vcmutes-none"));
            return;
        }

        foreach (var (userId, mute) in _voiceSystem.Mutes)
        {
            var playerName = _playerManager.TryGetSessionById(userId, out var session) ? session.Name : userId.ToString();
            var length = mute.Until == null
                ? Loc.GetString("ks-voice-mute-length-round")
                : Loc.GetString("ks-voice-mute-length-minutes",
                    ("minutes", (int)Math.Ceiling(Math.Max(0d, (mute.Until.Value - _gameTiming.RealTime).TotalMinutes))));

            shell.WriteLine(Loc.GetString("cmd-vcmutes-entry",
                ("player", playerName),
                ("length", length),
                ("admin", mute.AdminName),
                ("reason", mute.Reason)));
        }

        foreach (var (userId, remaining) in _autoMutes)
        {
            var playerName = _playerManager.TryGetSessionById(userId, out var session) ? session.Name : userId.ToString();
            shell.WriteLine(Loc.GetString("cmd-vcmutes-entry-auto",
                ("player", playerName),
                ("seconds", (int)Math.Ceiling(remaining.TotalSeconds))));
        }
    }
}
