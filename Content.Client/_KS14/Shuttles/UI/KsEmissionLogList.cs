using Content.Client.GameTicking.Managers;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Sensors;
using Content.Shared._KS14.Sensors.Prototypes;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Shuttles.UI;

/// <summary>
///     The emission-log ticker shared by the ELINT and RWR screens: chat-ordered
///         rows (newest at the bottom, scroll pinned there) coloured by event kind,
///         fresh rows flashing bright and settling (the decay anim runs on seconds
///         since the entry landed). The hosting screen provides its palette and
///         pushes the log on each state update.
/// </summary>
public sealed partial class KsEmissionLogList : BoxContainer
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private KsSensorHudPrototype? _hud;
    private KsSensorHudPrototype Hud => _hud ??= _proto.Index(KsSensorHudPrototype.DefaultId);

    private readonly List<(Label Row, TimeSpan Time)> _rows = new();

    /// <summary>
    ///     What the rows were last built from. The log is append-only with a
    ///         front trim, so count plus boundary timestamps identify it; skipping
    ///         unchanged pushes keeps the bottom pin from yanking the scroll away
    ///         from someone reading back through the history.
    /// </summary>
    private (int Count, TimeSpan First, TimeSpan Last) _signature = (-1, default, default);

    /// <summary>Frames left before the pin lands; see the note in Update.</summary>
    private int _pinFrames;

    public KsInstrumentPalette Palette { get; set; } = new();

    public KsEmissionLogList()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
    }

    public void Update(List<KsEmissionLogEntry>? log)
    {
        var signature = log is { Count: > 0 }
            ? (log.Count, log[0].Time, log[log.Count - 1].Time)
            : (0, default(TimeSpan), default(TimeSpan));
        if (signature == _signature)
            return;
        _signature = signature;

        RemoveAllChildren();
        _rows.Clear();

        if (log is not { Count: > 0 })
        {
            AddChild(new Label
            {
                Text = Loc.GetString("ks-emission-log-empty"),
                StyleClasses = { KsInstrumentSheetlet.StyleClassSmall },
                FontColorOverride = Palette.TextDim,
            });
            return;
        }

        // Entry times ride the server clock, the same one RoundStartTimeSpan is
        // measured on, so the difference IS the round clock at the moment the
        // event landed.
        var roundStart = _entManager.System<ClientGameTicker>().RoundStartTimeSpan;

        // Chat direction: oldest at the top, the newest entry landing at the
        // bottom where the eye already waits for it.
        foreach (var entry in log)
        {
            var label = entry.Designation ?? entry.Name ?? Loc.GetString("ks-emission-log-unknown-emitter");

            var (locId, color) = entry.Kind switch
            {
                KsEmissionLogKind.EmitterNew => ("ks-emission-log-emitter-new", Palette.Text),
                KsEmissionLogKind.EmitterSilent => ("ks-emission-log-emitter-silent", Palette.TextDim),
                KsEmissionLogKind.JamStart => ("ks-emission-log-jam-start", Palette.Warning),
                _ => ("ks-emission-log-jam-end", Palette.Good),
            };

            var age = entry.Time > roundStart ? entry.Time - roundStart : TimeSpan.Zero;
            var stamp = $"{(int) age.TotalHours:D2}:{age.Minutes:D2}:{age.Seconds:D2}";

            var row = new Label
            {
                Text = Loc.GetString("ks-emission-log-line",
                    ("stamp", stamp),
                    ("event", Loc.GetString(locId, ("label", label)))),
                StyleClasses = { KsInstrumentSheetlet.StyleClassSmall },
                FontColorOverride = color,
            };

            AddChild(row);
            _rows.Add((row, entry.Time));
        }

        // The hosting scroll's range only learns the new content height on the
        // next arrange, so pinning now would clamp against the stale maximum.
        // Wait out a couple of frames instead.
        _pinFrames = 2;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_pinFrames > 0)
        {
            _pinFrames--;
            if (Parent is ScrollContainer scroll)
                scroll.VScroll = float.MaxValue;
        }

        var reduced = _cfg.GetCVar(KsCCVars.HudReducedMotion);
        foreach (var (row, time) in _rows)
        {
            var since = Math.Max(0.0, (_timing.CurTime - time).TotalSeconds);
            var flash = Hud.LogFlash.Eval(since, reduced);
            row.Modulate = new Color(flash, flash, flash);
        }
    }
}
