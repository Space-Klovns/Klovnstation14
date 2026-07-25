using Content.Shared._KS14.Sensors;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Server.GameObjects;

namespace Content.Server._KS14.Sensors;

/// <summary>
///     Bridges the upstream shuttle and radar console state builders to the sensor
///         framework: a console raises <see cref="KsNavStateBuiltEvent"/> once its nav
///         state is built, and this system attaches the console grid's contact picture
///         (contacts, coverage fans, emitter state). Collecting here rather than inline in
///         ShuttleConsoleSystem keeps the upstream builder free of sensor logic beyond
///         that single raise.
/// </summary>
public sealed partial class KsSensorConsoleSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private KsSensorSystem _sensors = default!;
    [Dependency] private KsElintSystem _elint = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsNavStateBuiltEvent>(OnNavStateBuilt);

        // Scoped to the key the toggles are actually drawn on. A raw directed
        // subscription would also fire for a message sent on the console's OTHER
        // interface (WiresUiKey): the engine only checks that the sender is a
        // registered actor of the key the message arrived on, then raises to every
        // subscriber regardless of key. That would let a wires-panel actor bypass
        // UIRequiresLock (which lists only ShuttleConsoleUiKey) and
        // ActivatableUIRequiresPower (which only closes the ActivatableUI key).
        Subs.BuiEvents<KsSensorConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<KsToggleRadarMessage>(OnToggleRadar);
            subs.Event<KsToggleJammerMessage>(OnToggleJammer);
            subs.Event<KsElintFocusMessage>(OnElintFocus);
            subs.Event<KsElintClearFocusMessage>(OnElintClearFocus);
        });
    }

    /// <summary>
    ///     The KS console set is same-grid only (the drone variant is retired), so the
    ///         console's own grid is the grid whose state the buttons were built from.
    /// </summary>
    private void OnToggleRadar(Entity<KsSensorConsoleComponent> ent, ref KsToggleRadarMessage args)
    {
        if (Transform(ent.Owner).GridUid is { } gridUid)
            _sensors.ToggleGridRadar(gridUid);
    }

    private void OnToggleJammer(Entity<KsSensorConsoleComponent> ent, ref KsToggleJammerMessage args)
    {
        if (Transform(ent.Owner).GridUid is { } gridUid)
            _sensors.ToggleGridJammer(gridUid);
    }

    private void OnElintFocus(Entity<KsSensorConsoleComponent> ent, ref KsElintFocusMessage args)
    {
        if (Transform(ent.Owner).GridUid is not { } gridUid || !TryGetEntity(args.Target, out var target))
            return;

        // Fog-of-war gate: only an emitter this console currently ROSTERS may be focused
        // (designated, not tombstoned, this map: the same visibility test the snapshot
        // applies). Anything else would let a modified client probe records the roster
        // hides and read their existence, or a hidden ship's escaped-vs-destroyed fate,
        // off the observable focus state.
        if (!_sensors.CanFocusContact(gridUid, target.Value))
            return;

        _elint.SetGridFocus(gridUid, target.Value);
    }

    private void OnElintClearFocus(Entity<KsSensorConsoleComponent> ent, ref KsElintClearFocusMessage args)
    {
        if (Transform(ent.Owner).GridUid is { } gridUid)
            _elint.SetGridFocus(gridUid, null);
    }

    private void OnNavStateBuilt(ref KsNavStateBuiltEvent ev)
    {
        // Only fork sensor consoles get the contact picture. A vanilla shuttle
        // console has no marker, so its nav state stays exactly as upstream (draw
        // every grid, no sensor data) and it never pays for the coverage sweep.
        if (!HasComp<KsSensorConsoleComponent>(ev.Console))
            return;

        var gridXform = Transform(ev.Console);

        // Anchored consoles only (a handheld scanner or a pocket pAI is not part of
        // the ship's internal datalink), and only while a viewer has the UI open: the
        // stored BUI state PVS-replicates to nearby clients even while closed, so an
        // unwatched console must carry no contacts, and the coverage fan is an
        // occluder-raycast sweep not worth paying for a closed-UI push.
        if (!gridXform.Anchored
            || (!_ui.IsUiOpen(ev.Console, ShuttleConsoleUiKey.Key)
                && !_ui.IsUiOpen(ev.Console, RadarConsoleUiKey.Key)))
        {
            return;
        }

        var collectEv = new KsCollectNavContactsEvent(gridXform.GridUid);
        RaiseLocalEvent(ref collectEv);
        ev.State.KsSensorNav = new KsSensorNavState
        {
            Contacts = collectEv.Contacts,
            Regions = collectEv.Regions,
            Jammed = collectEv.Jammed,
            HasRadar = collectEv.HasRadar,
            RadarActive = collectEv.RadarActive,
            HasJammer = collectEv.HasJammer,
            JammerActive = collectEv.JammerActive,
            HasElint = collectEv.HasElint,
            ElintDeaf = collectEv.ElintDeaf,
            HasRwr = collectEv.HasRwr,
            EmissionLog = collectEv.EmissionLog,
        };
    }
}
