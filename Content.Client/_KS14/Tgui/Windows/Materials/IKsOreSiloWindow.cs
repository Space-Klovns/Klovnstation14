using Content.Shared.Materials.OreSilo;

namespace Content.Client._KS14.Tgui.Windows.Materials;

/// <summary>
///     What the silo's bound user interface needs of its window, so that the stock menu and the tgui one are interchangeable.
/// </summary>
public interface IKsOreSiloWindow
{
    event Action<NetEntity>? OnClientEntryPressed;

    void SetEntity(EntityUid uid);

    void Update(OreSiloBuiState state);
}
