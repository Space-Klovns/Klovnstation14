using Content.Shared._KS14.FieldCommander;
using Content.Shared._RMC14.Vendors;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._KS14.FieldCommander;

/// <summary>
///     Shows a field commander defender their points, and floats up what they just gained or lost.
/// </summary>
public sealed partial class KsFieldCommanderPointsSystem : EntitySystem
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IUserInterfaceManager _userInterfaceManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EntityQuery<KsFieldCommanderWaveStatusComponent> _waveStatusQuery = default!;
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

    /// <summary>
    ///     The wave line as last formatted, and what it was formatted from, the language included.
    /// </summary>
    private string? _waveText;
    private System.Globalization.CultureInfo? _waveTextCulture;
    private int _waveTextWave;
    private int? _waveTextSecondsLeft;

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

        var reason = args.Reason.Format(EntityManager);
        EnsureControl().AddChange(args.Delta, reason);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // The control stays once made, and only the total hides: a change can arrive before the state that carries the
        //      new total, or while the player has no body with points (a ghost, a fresh body), and dropping the control
        //      then would drop the change with it.
        if (TryGetLocalPoints(out var points))
        {
            var control = EnsureControl();
            control.SetTotal(points);
            control.SetWaveText(GetWaveText());
        }
        else
        {
            Control?.SetTotal(null);
            Control?.SetWaveText(null);
        }
    }

    /// <summary>
    ///     The wave line for the map the local player is on, if the commander has set one.
    /// </summary>
    private string? GetWaveText()
    {
        if (_playerManager.LocalEntity is not { } localUid ||
            !EntityManager.TransformQuery.TryComp(localUid, out var transformComponent) ||
            transformComponent.MapUid is not { } mapUid ||
            !_waveStatusQuery.TryComp(mapUid, out var statusComponent))
            return null;

        // Null for no wave scheduled. Whole seconds: the line only changes when one of these does.
        int? secondsLeft = statusComponent.NextWaveTime is { } nextWaveTime
            ? (int)Math.Ceiling(Math.Max(0d, (nextWaveTime - _gameTiming.CurTime).TotalSeconds))
            : null;

        // This runs every frame, and formatting a localised string every frame is a waste.
        var culture = Loc.DefaultCulture;
        if (_waveText != null &&
            _waveTextWave == statusComponent.Wave &&
            _waveTextSecondsLeft == secondsLeft &&
            ReferenceEquals(_waveTextCulture, culture))
            return _waveText;

        _waveTextCulture = culture;
        _waveTextWave = statusComponent.Wave;
        _waveTextSecondsLeft = secondsLeft;
        _waveText = FormatWaveText(statusComponent.Wave, secondsLeft);
        return _waveText;
    }

    private string FormatWaveText(int wave, int? secondsLeft)
    {
        if (secondsLeft is not { } seconds)
        {
            return wave > 0
                ? Loc.GetString("ks-fieldcommander-wave-status-none", ("wave", wave))
                : Loc.GetString("ks-fieldcommander-wave-status-idle");
        }

        var time = $"{seconds / 60}:{seconds % 60:00}";
        return wave > 0
            ? Loc.GetString("ks-fieldcommander-wave-status", ("wave", wave), ("time", time))
            : Loc.GetString("ks-fieldcommander-wave-status-first", ("time", time));
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
