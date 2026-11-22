using Content.Server.EUI;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._KS14.FieldCommander.Admin;

/// <summary>
///     Opens the field commander admin window, one per admin, and refreshes the status strip of each open one.
/// </summary>
public sealed partial class KsFieldCommanderAdminEuiSystem : EntitySystem
{
    /// <summary>
    ///     How often an open window's status strip is refreshed.
    /// </summary>
    public static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(1);

    [Dependency] private EuiManager _euiManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;

    private readonly Dictionary<ICommonSession, KsFieldCommanderAdminEui> _open = new();
    private readonly List<KsFieldCommanderAdminEui> _refreshing = new();
    private TimeSpan _nextStatusTime;

    public override void Shutdown()
    {
        base.Shutdown();

        _refreshing.Clear();
        _refreshing.AddRange(_open.Values);
        foreach (var eui in _refreshing)
            eui.Close();

        _open.Clear();
        _refreshing.Clear();
    }

    /// <summary>
    ///     Opens the window for this player, or brings an open one up to date.
    /// </summary>
    public KsFieldCommanderAdminEui OpenEui(ICommonSession session)
    {
        if (_open.TryGetValue(session, out var open))
        {
            open.StateDirty();
            return open;
        }

        var eui = new KsFieldCommanderAdminEui();
        _open.Add(session, eui);
        _euiManager.OpenEui(eui, session);
        return eui;
    }

    public bool TryGetEui(ICommonSession session, out KsFieldCommanderAdminEui? eui)
    {
        return _open.TryGetValue(session, out eui);
    }

    public void OnClosed(KsFieldCommanderAdminEui eui)
    {
        if (_open.TryGetValue(eui.Player, out var open) && open == eui)
            _open.Remove(eui.Player);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_open.Count == 0 || _gameTiming.CurTime < _nextStatusTime)
            return;

        _nextStatusTime = _gameTiming.CurTime + StatusInterval;

        // Closing one takes it out of the dictionary.
        _refreshing.Clear();
        _refreshing.AddRange(_open.Values);
        foreach (var eui in _refreshing)
        {
            if (!eui.IsAllowed())
            {
                eui.Close();
                continue;
            }

            eui.SendStatus();
        }

        _refreshing.Clear();
    }
}
