using Content.Client._KS14.AdminMusic;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Gameplay;
using JetBrains.Annotations;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client._KS14.UserInterface.Systems.AdminMusic;

[UsedImplicitly]
public sealed class KsAdminMusicUiController : UIController
{
    [Dependency] private KsAdminMusicManager _adminMusicManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        var gameplayStateLoad = UIManager.GetUIController<GameplayStateLoadController>();

        gameplayStateLoad.OnScreenLoad += OnScreenLoad;
        gameplayStateLoad.OnScreenUnload += OnScreenUnload;
    }

    private void OnScreenLoad()
    {
        switch (UIManager.ActiveScreen)
        {
            // yea totally reused
            case DefaultGameScreen game:
                _adminMusicManager.SetPopupContainer(game.VoteMenu);
                break;
            case SeparatedChatGameScreen separated:
                _adminMusicManager.SetPopupContainer(separated.VoteMenu);
                break;
        }

        _adminMusicManager.TryPopulatePopupContainer();
    }

    private void OnScreenUnload()
    {
        _adminMusicManager.TryClearPopupContainer();
    }
}
