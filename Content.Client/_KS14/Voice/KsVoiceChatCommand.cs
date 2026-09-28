using Robust.Client.UserInterface;
using Robust.Shared.Console;

namespace Content.Client._KS14.Voice;

/// <summary>
///     <c>voicechat</c>: opens the voice link window.
/// </summary>
public sealed partial class KsVoiceChatCommand : LocalizedCommands
{
    [Dependency] private IUserInterfaceManager _userInterfaceManager = default!;

    public override string Command => "voicechat";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _userInterfaceManager.GetUIController<KsVoiceUIController>().OpenWindow();
    }
}
