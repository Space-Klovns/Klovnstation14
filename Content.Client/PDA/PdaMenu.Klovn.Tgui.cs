using Content.Client._KS14.Tgui.Windows.Pda;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.PDA;

// The stock window stands in for the tgui one behind the same interface.
public sealed partial class PdaMenu : IKsPdaMenu
{
    BaseButton IKsPdaMenu.FlashLightToggleButton => FlashLightToggleButton;
    BaseButton IKsPdaMenu.EjectIdButton => EjectIdButton;
    BaseButton IKsPdaMenu.EjectPenButton => EjectPenButton;
    BaseButton IKsPdaMenu.EjectPaiButton => EjectPaiButton;
    BaseButton IKsPdaMenu.ActivateMusicButton => ActivateMusicButton;
    BaseButton IKsPdaMenu.AccessRingtoneButton => AccessRingtoneButton;
    BaseButton IKsPdaMenu.ShowUplinkButton => ShowUplinkButton;
    BaseButton IKsPdaMenu.LockUplinkButton => LockUplinkButton;
    Control IKsPdaMenu.ProgramView => ProgramView;

    void IKsPdaMenu.AddProgramCloseHandler(Action handler)
    {
        ProgramCloseButton.OnPressed += _ => handler();
    }
}
