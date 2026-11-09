namespace Content.Client._KS14.Tgui.Windows.Apc;

/// <summary>
///     What the APC's bound user interface needs of its window, so that the stock menu and the tgui one are interchangeable.
/// </summary>
public interface IKsApcWindow : IDisposable
{
    event Action? OnBreaker;

    void SetEntity(EntityUid entity);

    void SetAccessEnabled(bool hasAccess);

    void UpdateState(BoundUserInterfaceState state);
}
