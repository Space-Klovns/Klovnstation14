using Content.Shared._KS14.FieldCommander;
using Content.Shared._RMC14.Vendors;
using Robust.Client.Player;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.FieldCommander;

/// <summary>
///     Shows a field commander defender their points, and floats up what they just gained or lost.
/// </summary>
public sealed partial class KsFieldCommanderPointsSystem : EntitySystem
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IUserInterfaceManager _userInterfaceManager = default!;
    [Dependency] private EntityQuery<CMVendorUserComponent> _vendorUserQuery = default!;

    /// <summary>
    ///     The on-screen points readout, once anything has needed it.
    /// </summary>
    public KsFieldCommanderPointsControl? Control { get; private set; }

    /// <summary>
    ///     The latest change this client was told of, for tests.
    /// </summary>
    [ViewVariables]
    public KsFieldCommanderPointsChangedEvent? LastChange { get; private set; }

    public override void Shutdown()
    {
        base.Shutdown();

        Control?.Orphan();
        Control = null;
    }

    [SubscribeNetworkEvent]
    private void OnPointsChanged(KsFieldCommanderPointsChangedEvent args)
    {
        LastChange = args;

        var reason = Loc.GetString(args.Reason, ("name", args.ReasonName ?? string.Empty));
        EnsureControl().AddChange(args.Delta, reason);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // The control stays once made, and only the total hides: a change can arrive before the state that carries the
        //      new total, or while the player has no body with points (a ghost, a fresh body), and dropping the control
        //      then would drop the change with it.
        if (TryGetLocalPoints(out var points))
            EnsureControl().SetTotal(points);
        else
            Control?.SetTotal(null);
    }

    /// <summary>
    ///     The local player's field commander points, if their body has any.
    /// </summary>
    public bool TryGetLocalPoints(out int points)
    {
        points = 0;

        if (_playerManager.LocalEntity is not { } localUid ||
            !_vendorUserQuery.TryComp(localUid, out var vendorUserComponent))
            return false;

        var extraPoints = vendorUserComponent.ExtraPoints;
        return extraPoints != null && extraPoints.TryGetValue(KsFieldCommanderPoints.PointsType, out points);
    }

    private KsFieldCommanderPointsControl EnsureControl()
    {
        if (Control is { Parent: not null } control)
            return control;

        Control = new KsFieldCommanderPointsControl();
        _userInterfaceManager.RootControl.AddChild(Control);
        return Control;
    }
}
