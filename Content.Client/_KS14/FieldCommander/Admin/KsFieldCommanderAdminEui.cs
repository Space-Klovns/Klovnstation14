using Content.Client.Eui;
using Content.Shared._KS14.FieldCommander;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._KS14.FieldCommander.Admin;

/// <summary>
///     The field commander admin window's connection to the server: state and the status refresh in, the admin's
///         changes out.
/// </summary>
[UsedImplicitly]
public sealed class KsFieldCommanderAdminEui : BaseEui
{
    private readonly KsFieldCommanderAdminWindow _window;

    public KsFieldCommanderAdminEui()
    {
        _window = new KsFieldCommanderAdminWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnAdminMessage += SendMessage;
    }

    public override void Opened()
    {
        base.Opened();
        _window.OpenCentered();
    }

    public override void HandleState(EuiStateBase state)
    {
        base.HandleState(state);

        if (state is KsFieldCommanderAdminEuiState adminState)
            _window.UpdateState(adminState);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        switch (msg)
        {
            case KsFieldCommanderAdminStatusMessage status:
                _window.UpdateStatus(status.Rule, status.Status);
                break;
            case KsFieldCommanderAdminResultMessage result:
                _window.ShowResult(result.Accepted);
                break;
        }
    }

    public override void Closed()
    {
        base.Closed();
        _window.Close();
    }
}
