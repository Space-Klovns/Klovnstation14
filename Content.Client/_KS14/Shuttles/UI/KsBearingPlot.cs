using System.Numerics;
using Content.Client.Resources;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Sensors;
using Content.Shared._KS14.Sensors.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Shuttles.UI;

/// <summary>
///     One entry on a <see cref="KsBearingPlot"/>, precomputed by the hosting
///         screen: either a bearing wedge (strobe set) or a positioned dot (world
///         position set), never both.
/// </summary>
public struct KsBearingPlotEntry
{
    public NetEntity Grid;

    /// <summary>The marker label: the emitter designation, or a name when known.</summary>
    public string Label;

    /// <summary>
    ///     The live draw colour (tier colour, undimmed): the plot itself handles the
    ///         memory-ghost treatment, afterglow while live and a timed fade toward
    ///         the dimmed tone once <see cref="GhostSince"/> is set.
    /// </summary>
    public Color Color;

    public bool Live;

    /// <summary>When the contact last refreshed; drives the phosphor afterglow.</summary>
    public TimeSpan LastSeen;

    /// <summary>When the contact turned ghost (client-observed); anchors the live-to-dim fade. Null = no fade, ghosts draw fully dimmed.</summary>
    public TimeSpan? GhostSince;

    /// <summary>When the contact-ping ring started (first seen / live again); null = no ping.</summary>
    public TimeSpan? PingStart;

    /// <summary>The single collapsed strobe for a bearing-quality contact.</summary>
    public KsBearingLine? Strobe;

    /// <summary>World position for an Exact-quality contact (triangulated/analysed emitters).</summary>
    public Vector2? WorldPosition;
}

/// <summary>
///     A circular bearing plot: compass ring, degree ticks, own ship at the
///         centre, bearing wedges radiating toward heard emitters and dots for the
///         ones with an earned fix. North-up by default (the ELINT convention); a
///         host that sets <see cref="PlotRotation"/> gets ship-relative BOW-up (the
///         RWR convention), where the ring reads as relative bearing (000 = bow)
///         and <see cref="OwnGrid"/> draws the own hull's outline at the centre in
///         place of the cross.
///     Pure presentation over the contact states the console already received: the
///         wedges' geometry is the server-approved strobe, nothing is derived
///         client-side that the server withheld.
/// </summary>
public sealed partial class KsBearingPlot : Control
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>Margin between the compass ring and the control edge, virtual px (room for cardinal labels).</summary>
    private const float RingMargin = 14f;

    private readonly Font _font;
    private KsSensorHudPrototype? _hud;
    private KsSensorHudPrototype Hud => _hud ??= _proto.Index(KsSensorHudPrototype.DefaultId);

    private readonly List<(Vector2 Pos, NetEntity Grid)> _hits = new();

    public List<KsBearingPlotEntry> Entries { get; } = new();

    /// <summary>The screen's palette; the plot draws its ring/ticks/labels from it.</summary>
    public KsInstrumentPalette Palette { get; set; } = new();

    /// <summary>Highlighted (roster-selected) contact, if any.</summary>
    public NetEntity? Selected { get; set; }

    /// <summary>The contact under focus analysis: gets the rotating reticle.</summary>
    public NetEntity? Focused { get; set; }

    /// <summary>World range mapped onto the ring radius, for placing positioned dots.</summary>
    public float RangeMeters { get; set; } = 512f;

    /// <summary>The own ship's world position (centre of the plot), pushed by the hosting screen each frame.</summary>
    public Vector2 OwnWorldPosition { get; set; }

    /// <summary>
    ///     World rotation subtracted from every plotted direction before projecting.
    ///         Zero = north-up. A host pushing the own grid's world rotation each
    ///         frame gets BOW-up: the ship's forward always points to the ring's 000.
    /// </summary>
    public Angle PlotRotation { get; set; }

    /// <summary>
    ///     When set, the own grid whose hull outline is drawn at the plot centre in
    ///         place of the own-ship cross, scaled to fit the dial's middle and
    ///         rotated with <see cref="PlotRotation"/> (so BOW-up shows the hull
    ///         upright). Own knowledge only: this is the ship the console rides.
    /// </summary>
    public EntityUid? OwnGrid { get; set; }

    public event Action<NetEntity>? OnEntrySelected;

    /// <summary>
    ///     Grid-local boundary-edge segments of <see cref="OwnGrid"/>'s tiles (a
    ///         line list), cached until the grid's tiles change. Built here rather
    ///         than reusing the radar's DrawGrid path: inheriting the whole
    ///         shuttle-control machinery for one small centred silhouette would drag
    ///         zoom/pan state into a plain control.
    /// </summary>
    private readonly List<Vector2> _hullEdges = new();
    private Vector2[] _hullScratch = Array.Empty<Vector2>();
    private EntityUid _hullGrid;
    private GameTick _hullBuiltTick;

    private SharedMapSystem? _maps;
    private SharedTransformSystem? _xformSystem;

    /// <summary>Hulls with more boundary edges than this are stations, not ships; they keep the cross.</summary>
    private const int MaxHullEdges = 4096;

    public KsBearingPlot()
    {
        IoCManager.InjectDependencies(this);
        _font = IoCManager.Resolve<IResourceCache>()
            .GetFont(KsInstrumentSheetlet.FontPath, KsInstrumentSheetlet.FontSizeBody);
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
    }

    /// <summary>
    ///     A mathematical direction angle as compass display degrees: 0 = world +Y,
    ///         clockwise, east = 090 (same convention as the radar readouts).
    /// </summary>
    public static float CompassDegrees(Angle bearing)
    {
        var deg = 90f - (float) bearing.Degrees;
        return (deg % 360f + 360f) % 360f;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick || _hits.Count == 0)
            return;

        var mouse = args.RelativePosition * UIScale;
        var bestDist = 14f * UIScale;
        NetEntity? best = null;

        foreach (var (pos, grid) in _hits)
        {
            var dist = (pos - mouse).Length();
            if (dist < bestDist)
            {
                bestDist = dist;
                best = grid;
            }
        }

        if (best is { } picked)
        {
            OnEntrySelected?.Invoke(picked);
            args.Handle();
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        _hits.Clear();

        var size = (Vector2) PixelSize;
        var centre = size / 2f;
        var radius = MathF.Min(size.X, size.Y) / 2f - RingMargin * UIScale;
        if (radius < 24f)
            return;

        var reduced = _cfg.GetCVar(KsCCVars.HudReducedMotion);
        var now = _timing.CurTime.TotalSeconds;

        DrawRing(handle, centre, radius);

        if (!TryDrawOwnHull(handle, centre, radius))
        {
            var cross = 4f * UIScale;
            handle.DrawLine(centre - new Vector2(cross, 0f), centre + new Vector2(cross, 0f), Palette.Good);
            handle.DrawLine(centre - new Vector2(0f, cross), centre + new Vector2(0f, cross), Palette.Good);
        }

        var scale = radius / MathF.Max(1f, RangeMeters);

        foreach (var entry in Entries)
        {
            // The plot, not the screen, owns the presentation shading.
            var shaded = entry;
            shaded.Color = DisplayColor(entry, now, reduced);

            if (entry.Strobe is { } strobe)
                DrawWedge(handle, centre, radius, scale, shaded, strobe, now, reduced);
            else if (entry.WorldPosition is { } world)
                DrawFix(handle, centre, radius, scale, shaded, world, now, reduced);
        }

        DrawOverlays(handle, now, reduced);
    }

    /// <summary>
    ///     The entry's colour as drawn this frame: the live tier colour riding the
    ///         phosphor afterglow (fresh sweeps flash hotter), or the memory-ghost
    ///         tone, reached through a short fade when the flip was observed.
    ///         Entries without the timing fields degrade to the steady colours.
    /// </summary>
    private Color DisplayColor(in KsBearingPlotEntry entry, double now, bool reduced)
    {
        if (entry.Live)
        {
            var glow = Hud.Afterglow.Eval(Math.Max(0d, now - entry.LastSeen.TotalSeconds), reduced);
            var col = entry.Color;
            return new Color(
                Math.Clamp(col.R * glow, 0f, 1f),
                Math.Clamp(col.G * glow, 0f, 1f),
                Math.Clamp(col.B * glow, 0f, 1f),
                col.A);
        }

        var dimmed = Hud.Dim(entry.Color);
        if (entry.GhostSince is not { } ghostAt || reduced || Hud.GhostFadeSeconds <= 0f)
            return dimmed;

        var fade = (float) Math.Clamp((now - ghostAt.TotalSeconds) / Hud.GhostFadeSeconds, 0d, 1d);
        return Color.InterpolateBetween(entry.Color, dimmed, fade);
    }

    private void DrawPing(DrawingHandleScreen handle, Vector2 pos, in KsBearingPlotEntry entry, double now, bool reduced)
    {
        if (entry.PingStart is not { } start || reduced || Hud.PingSeconds <= 0f)
            return;

        var progress = (float) ((now - start.TotalSeconds) / Hud.PingSeconds);
        KsInstrumentDraw.DrawPing(handle, pos, progress, entry.Color, UIScale, Hud.PingAlpha);
    }

    private void DrawRing(DrawingHandleScreen handle, Vector2 centre, float radius)
    {
        KsInstrumentDraw.DrawRingTicks(handle, centre, radius, UIScale, Palette.AccentDim, Palette.AccentDim, Palette.Accent);

        var cardinals = new[] { (Deg: 0, Text: "000"), (Deg: 90, Text: "090"), (Deg: 180, Text: "180"), (Deg: 270, Text: "270") };
        foreach (var (deg, text) in cardinals)
        {
            var dir = new Vector2(MathF.Sin(MathHelper.DegreesToRadians(deg)), -MathF.Cos(MathHelper.DegreesToRadians(deg)));
            var dim = handle.GetDimensions(_font, text, UIScale * 0.8f);

            // Push the label centre out past the ring by half its own extent along
            // the direction, then convert to the top-left DrawString anchor.
            var labelCentre = centre + dir * (radius + 2f * UIScale)
                + dir * new Vector2(dim.X / 2f, dim.Y / 2f) * new Vector2(MathF.Abs(dir.X), MathF.Abs(dir.Y));
            var pos = Vector2.Clamp(labelCentre - dim / 2f, Vector2.Zero, Vector2.Max(Vector2.Zero, (Vector2) PixelSize - dim));
            handle.DrawString(_font, pos, text, UIScale * 0.8f, Palette.TextDim);
        }
    }

    private void DrawWedge(DrawingHandleScreen handle, Vector2 centre, float radius, float scale, in KsBearingPlotEntry entry, in KsBearingLine strobe, double now, bool reduced)
    {
        // The apex is where the measuring grid sits relative to us: the centre for an
        // own-ship strobe, the (Exact-known) ally's plotted position for a relay.
        var rel = (-PlotRotation).RotateVec(strobe.Origin - OwnWorldPosition);
        var apex = centre + new Vector2(rel.X, -rel.Y) * scale;

        // Keep the apex inside the ring so the wedge always reads on the dial.
        var fromCentre = apex - centre;
        var maxApex = radius - 2f;
        if (fromCentre.Length() > maxApex)
            apex = centre + Vector2.Normalize(fromCentre) * maxApex;

        var bearing = (float) (strobe.Bearing - PlotRotation).Theta;
        var half = MathHelper.DegreesToRadians(MathF.Max(0.5f, strobe.AccuracyDeg));

        // A strong signal holds steady; a faint one breathes with the shimmer anim.
        var shimmer = float.Lerp(Hud.WedgeShimmer.Eval(now, reduced), 1f, Math.Clamp(strobe.SignalStrength, 0f, 1f));

        var centreEnd = RayToRing(centre, radius, apex, ScreenDir(bearing));

        // The filled wedge is an emission claim, so once the track goes stale it
        // fades out with the ghost transition instead of squatting on the dial
        // forever; the rim marker and label below stay as the remembered bearing,
        // matching the nav scope's treatment.
        var wedgeAlpha = 1f;
        if (!entry.Live)
        {
            wedgeAlpha = entry.GhostSince is { } ghostAt && !reduced && Hud.GhostFadeSeconds > 0f
                ? 1f - (float) Math.Clamp((now - ghostAt.TotalSeconds) / Hud.GhostFadeSeconds, 0d, 1d)
                : 0f;
        }

        if (wedgeAlpha > 0f)
        {
            var leftEnd = RayToRing(centre, radius, apex, ScreenDir(bearing - half));
            var rightEnd = RayToRing(centre, radius, apex, ScreenDir(bearing + half));

            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, new[] { apex, leftEnd, rightEnd }, entry.Color.WithAlpha(Hud.ConeFillAlpha * 2f * shimmer * wedgeAlpha));
            handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, new[] { leftEnd, apex, rightEnd }, entry.Color.WithAlpha(Hud.ConeLineAlpha * shimmer * wedgeAlpha));
            handle.DrawLine(apex, centreEnd, entry.Color.WithAlpha(shimmer * wedgeAlpha));
        }

        // Marker + label where the centreline meets the ring: range is unknown, so
        // the rim is the honest place to pin the emitter's mark.
        handle.DrawCircle(centreEnd, 3f * UIScale, entry.Color, entry.Live);
        DrawMarkerLabel(handle, centre, centreEnd, entry);
        DrawPing(handle, centreEnd, entry, now, reduced);
        _hits.Add((centreEnd, entry.Grid));
    }

    private void DrawFix(DrawingHandleScreen handle, Vector2 centre, float radius, float scale, in KsBearingPlotEntry entry, Vector2 world, double now, bool reduced)
    {
        var rel = (-PlotRotation).RotateVec(world - OwnWorldPosition);
        var pos = centre + new Vector2(rel.X, -rel.Y) * scale;

        var fromCentre = pos - centre;
        var beyond = fromCentre.Length() > radius - 2f;
        if (beyond)
            pos = centre + Vector2.Normalize(fromCentre) * (radius - 2f);

        // A fix inside the dial is a filled dot; one past the plot range pins to the
        // rim as a hollow ring (direction true, range off the dial).
        handle.DrawCircle(pos, 3.5f * UIScale, entry.Color, filled: !beyond && entry.Live);
        DrawMarkerLabel(handle, centre, pos, entry);
        DrawPing(handle, pos, entry, now, reduced);
        _hits.Add((pos, entry.Grid));
    }

    private void DrawMarkerLabel(DrawingHandleScreen handle, Vector2 centre, Vector2 marker, in KsBearingPlotEntry entry)
    {
        if (entry.Label.Length == 0)
            return;

        var dim = handle.GetDimensions(_font, entry.Label, UIScale * 0.9f);

        // Offset the label outward from the centre so it clears the marker, then
        // clamp inside the control.
        var dir = marker - centre;
        var outward = dir.LengthSquared() > 0f ? Vector2.Normalize(dir) : -Vector2.UnitY;
        var pos = marker + outward * 6f * UIScale - dim / 2f;

        var extents = Vector2.Max(Vector2.Zero, (Vector2) PixelSize - dim);
        pos = Vector2.Clamp(pos, Vector2.Zero, extents);

        handle.DrawString(_font, pos, entry.Label, UIScale * 0.9f, entry.Color);
    }

    /// <summary>Selection diamond + focus reticle pass, drawn over every marker.</summary>
    private void DrawOverlays(DrawingHandleScreen handle, double now, bool reduced)
    {
        foreach (var (pos, grid) in _hits)
        {
            if (grid == Selected)
            {
                var r = 7f * UIScale;
                var diamond = new[]
                {
                    pos + new Vector2(0f, -r),
                    pos + new Vector2(r, 0f),
                    pos + new Vector2(0f, r),
                    pos + new Vector2(-r, 0f),
                    pos + new Vector2(0f, -r),
                };
                handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, diamond, Palette.Text);
            }

            if (grid == Focused)
            {
                // Rotating corner brackets: four short chords riding the sweep anim.
                var angle = Hud.FocusReticle.Eval(now, reduced) * MathF.Tau;
                var r = 10f * UIScale;
                for (var k = 0; k < 4; k++)
                {
                    var a = angle + k * MathF.PI / 2f;
                    var p1 = pos + r * new Vector2(MathF.Cos(a - 0.3f), MathF.Sin(a - 0.3f));
                    var p2 = pos + r * new Vector2(MathF.Cos(a + 0.3f), MathF.Sin(a + 0.3f));
                    handle.DrawLine(p1, p2, Palette.Good);
                }
            }
        }
    }

    /// <summary>
    ///     Draws <see cref="OwnGrid"/>'s hull outline centred on the dial, scaled to
    ///         its middle and rotated by the grid's world rotation minus
    ///         <see cref="PlotRotation"/> (BOW-up therefore shows it upright). False
    ///         when there is no drawable hull (no grid, station-sized, degenerate),
    ///         so the caller can fall back to the cross.
    /// </summary>
    private bool TryDrawOwnHull(DrawingHandleScreen handle, Vector2 centre, float radius)
    {
        if (OwnGrid is not { } gridUid
            || !_entManager.TryGetComponent(gridUid, out MapGridComponent? grid))
            return false;

        RebuildHullCache(gridUid, grid);
        if (_hullEdges.Count == 0)
            return false;

        // The plot centre is the ship's centre of mass (OwnWorldPosition tracks it),
        // so the outline must be drawn relative to the same point.
        var localCentre = _entManager.TryGetComponent(gridUid, out PhysicsComponent? physics)
            ? physics.LocalCenter
            : grid.LocalAABB.Center;

        var extent = MathF.Max(
            MathF.Max(MathF.Abs(grid.LocalAABB.Left - localCentre.X), MathF.Abs(grid.LocalAABB.Right - localCentre.X)),
            MathF.Max(MathF.Abs(grid.LocalAABB.Bottom - localCentre.Y), MathF.Abs(grid.LocalAABB.Top - localCentre.Y)));
        if (extent <= 0f)
            return false;

        var hullScale = radius * 0.2f / extent;
        _xformSystem ??= _entManager.System<SharedTransformSystem>();
        var rot = _xformSystem.GetWorldRotation(gridUid) - PlotRotation;

        if (_hullScratch.Length < _hullEdges.Count)
            _hullScratch = new Vector2[_hullEdges.Count];

        for (var i = 0; i < _hullEdges.Count; i++)
        {
            var v = rot.RotateVec(_hullEdges[i] - localCentre) * hullScale;
            _hullScratch[i] = centre + new Vector2(v.X, -v.Y);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.LineList, _hullScratch.AsSpan(0, _hullEdges.Count), Palette.Good);
        return true;
    }

    /// <summary>
    ///     Rebuilds the cached hull outline when the grid (or its tiles) changed: the
    ///         boundary edges of the tile set, each tile side with no same-grid
    ///         neighbour, as grid-local line segments.
    /// </summary>
    private void RebuildHullCache(EntityUid gridUid, MapGridComponent grid)
    {
        if (_hullGrid == gridUid && _hullBuiltTick >= grid.LastTileModifiedTick)
            return;

        _hullGrid = gridUid;
        _hullBuiltTick = grid.LastTileModifiedTick;
        _hullEdges.Clear();

        _maps ??= _entManager.System<SharedMapSystem>();
        var tiles = new HashSet<Vector2i>();
        var rator = _maps.GetAllTilesEnumerator(gridUid, grid);
        while (rator.MoveNext(out var tileRef))
        {
            tiles.Add(tileRef.Value.GridIndices);
        }

        float s = grid.TileSize;
        foreach (var t in tiles)
        {
            if (!tiles.Contains(t + Vector2i.Left))
            {
                _hullEdges.Add(new Vector2(t.X * s, t.Y * s));
                _hullEdges.Add(new Vector2(t.X * s, (t.Y + 1) * s));
            }

            if (!tiles.Contains(t + Vector2i.Right))
            {
                _hullEdges.Add(new Vector2((t.X + 1) * s, t.Y * s));
                _hullEdges.Add(new Vector2((t.X + 1) * s, (t.Y + 1) * s));
            }

            if (!tiles.Contains(t + Vector2i.Down))
            {
                _hullEdges.Add(new Vector2(t.X * s, t.Y * s));
                _hullEdges.Add(new Vector2((t.X + 1) * s, t.Y * s));
            }

            if (!tiles.Contains(t + Vector2i.Up))
            {
                _hullEdges.Add(new Vector2(t.X * s, (t.Y + 1) * s));
                _hullEdges.Add(new Vector2((t.X + 1) * s, (t.Y + 1) * s));
            }

            // Past the cap this is a station, not a ship: the outline would be an
            // unreadable smudge at dial scale, so the cross takes over.
            if (_hullEdges.Count > MaxHullEdges * 2)
            {
                _hullEdges.Clear();
                return;
            }
        }
    }

    /// <summary>Math direction angle to north-up screen direction (world +Y = screen up).</summary>
    private static Vector2 ScreenDir(float mathAngle) => new(MathF.Cos(mathAngle), -MathF.Sin(mathAngle));

    /// <summary>
    ///     Where the ray from <paramref name="apex"/> along <paramref name="dir"/>
    ///         meets the compass ring: the positive root of the ray/circle
    ///         intersection (the apex is always kept inside the ring).
    /// </summary>
    private static Vector2 RayToRing(Vector2 centre, float radius, Vector2 apex, Vector2 dir)
    {
        var toApex = apex - centre;
        var b = Vector2.Dot(dir, toApex);
        var c = toApex.LengthSquared() - radius * radius;
        var disc = MathF.Max(0f, b * b - c);
        var t = -b + MathF.Sqrt(disc);
        return apex + dir * t;
    }
}
