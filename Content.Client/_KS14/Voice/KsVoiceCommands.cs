using Content.Shared.Administration;
using Robust.Client.UserInterface;
using Robust.Shared.Console;

namespace Content.Client._KS14.Voice;

/// <summary>
///     <c>voicechat</c>: opens the voice link window. Available to every player.
/// </summary>
[AnyCommand]
public sealed partial class KsVoiceChatCommand : LocalizedCommands
{
    [Dependency] private IUserInterfaceManager _userInterfaceManager = default!;

    public override string Command => "voicechat";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!_userInterfaceManager.GetUIController<KsVoiceUIController>().OpenWindow())
            shell.WriteError(Loc.GetString("ks-voice-link-error-disabled"));
    }
}

/// <summary>
///     <c>voicelink</c>: opens the voice page in the browser directly, skipping the window. Available to every player.
/// </summary>
[AnyCommand]
public sealed partial class KsVoiceLinkCommand : LocalizedCommands
{
    [Dependency] private IUserInterfaceManager _userInterfaceManager = default!;

    public override string Command => "voicelink";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!_userInterfaceManager.GetUIController<KsVoiceUIController>().OpenLinkInBrowser())
            shell.WriteError(Loc.GetString("ks-voice-link-error-disabled"));
    }
}
