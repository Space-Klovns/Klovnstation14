using Content.Client.Eui;
using Content.Shared._KS14.Procedural.Editor;
using Content.Shared.Eui;

namespace Content.Client._KS14.Procedural.Editor;

public sealed class KsProcgenThemeEditorEui : BaseEui
{
    private readonly KsProcgenThemeEditorWindow _window = new();

    public KsProcgenThemeEditorEui()
    {
        _window.OnClose += OnClose;
        _window.SaveRequested += Save;
    }

    public override void Opened() => _window.OpenCentered();

    public override void Closed()
    {
        _window.OnClose -= OnClose;
        _window.SaveRequested -= Save;
        _window.Close();
        base.Closed();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is KsProcgenThemeEditorState theme) _window.ApplyState(theme);
    }

    private void OnClose() => SendMessage(new CloseEuiMessage());
    private void Save(int revision, KsProcgenThemeEditorValues values) =>
        SendMessage(new KsProcgenThemeEditorSave { ExpectedRevision = revision, Values = values });
}
