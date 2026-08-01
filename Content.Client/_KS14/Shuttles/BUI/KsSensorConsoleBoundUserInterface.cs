using Content.Client._KS14.Shuttles.UI;
using Content.Shared._KS14.Sensors;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Events;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Map;

namespace Content.Client._KS14.Shuttles.BUI;

/// <summary>
///     Fork counterpart of the upstream ShuttleConsoleBoundUserInterface, bound to a KS14
///         console prototype's ShuttleConsoleUiKey in YAML so that console opens a
///         <see cref="KsInstrumentWindow"/> instead of the vanilla window. FTL and docking
///         wiring is identical to upstream.
///     <para>
///         The class name must NOT end with "ShuttleConsoleBoundUserInterface": the
///             client resolves a console's BUI from its ClientType string via
///             IReflectionManager.LooseGetType, which matches on FullName.EndsWith,
///             so such a name would hijack the vanilla console's lookup and force it
///             to open this window too. See KsConsoleBuiResolutionTest.
///     </para>
/// </summary>
[UsedImplicitly]
public sealed partial class KsSensorConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private KsInstrumentWindow? _window;

    public KsSensorConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<KsInstrumentWindow>();

        // Before any state lands, so the face never flashes default switch
        // positions before snapping to the remembered ones.
        _window.KsLoadPrefs(Owner);

        _window.RequestFTL += OnFTLRequest;
        _window.RequestBeaconFTL += OnFTLBeaconRequest;
        _window.DockRequest += OnDockRequest;
        _window.UndockRequest += OnUndockRequest;
        _window.RequestToggleRadar += OnToggleRadarRequest;
        _window.RequestToggleJammer += OnToggleJammerRequest;
        _window.RequestElintFocus += OnElintFocusRequest;
        _window.RequestElintClearFocus += OnElintClearFocusRequest;
    }

    private void OnToggleRadarRequest()
    {
        SendMessage(new KsToggleRadarMessage());
    }

    private void OnToggleJammerRequest()
    {
        SendMessage(new KsToggleJammerMessage());
    }

    private void OnElintFocusRequest(NetEntity target)
    {
        SendMessage(new KsElintFocusMessage { Target = target });
    }

    private void OnElintClearFocusRequest()
    {
        SendMessage(new KsElintClearFocusMessage());
    }

    private void OnUndockRequest(NetEntity entity)
    {
        SendMessage(new UndockRequestMessage()
        {
            DockEntity = entity,
        });
    }

    private void OnDockRequest(NetEntity entity, NetEntity target)
    {
        SendMessage(new DockRequestMessage()
        {
            DockEntity = entity,
            TargetDockEntity = target,
        });
    }

    private void OnFTLBeaconRequest(NetEntity ent, Angle angle)
    {
        SendMessage(new ShuttleConsoleFTLBeaconMessage()
        {
            Beacon = ent,
            Angle = angle,
        });
    }

    private void OnFTLRequest(MapCoordinates obj, Angle angle)
    {
        SendMessage(new ShuttleConsoleFTLPositionMessage()
        {
            Coordinates = obj,
            Angle = angle,
        });
    }

    protected override void Dispose(bool disposing)
    {
        // Popout teardown must run BEFORE base.Dispose: the base disposes the registered
        // window control, and orphaning a disposed control asserts, bailing out before the
        // OS window is destroyed and leaking a stale floating window (the way the vanilla
        // ahelp/admin-camera popouts do).
        if (disposing)
            _window?.TearDownPopout();

        base.Dispose(disposing);

        if (disposing)
            _window?.Dispose();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not ShuttleBoundUserInterfaceState cState)
            return;

        _window?.UpdateState(Owner, cState);
    }
}
