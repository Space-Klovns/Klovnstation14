using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Windows.Pda;

/// <summary>
///     What the PDA's bound user interface needs from its window, so that the stock <c>PdaMenu</c> and the tgui window
///         can stand in for each other.
/// </summary>
public interface IKsPdaMenu
{
    BaseButton FlashLightToggleButton { get; }
    BaseButton EjectIdButton { get; }
    BaseButton EjectPenButton { get; }
    BaseButton EjectPaiButton { get; }
    BaseButton ActivateMusicButton { get; }
    BaseButton AccessRingtoneButton { get; }
    BaseButton ShowUplinkButton { get; }
    BaseButton LockUplinkButton { get; }

    /// <summary>
    ///     Where a program's UI fragment is attached.
    /// </summary>
    Control ProgramView { get; }

    string? BorderColor { set; }
    string? AccentHColor { set; }
    string? AccentVColor { set; }

    event Action<EntityUid>? OnProgramItemPressed;
    event Action<EntityUid>? OnUninstallButtonPressed;
    event Action<EntityUid>? OnInstallButtonPressed;

    /// <summary>
    ///     The player closed the running program, as opposed to only minimising it.
    /// </summary>
    void AddProgramCloseHandler(Action handler);

    void UpdateState(PdaUpdateState state);
    void UpdateAvailablePrograms(List<(EntityUid, CartridgeComponent)> programs);
    void ToHomeScreen();
    void HideProgramHeader();
    void ToProgramView(string title);
}
