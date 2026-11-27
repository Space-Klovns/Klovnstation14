using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client.Gameplay;
using Content.Shared._KS14.Damage.Pending;
using Content.Shared._KS14.Projectiles.Diagnostics;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.State;
using Robust.Shared.Console;
using Robust.Shared.Map;

namespace Content.Client._KS14.Projectiles.Diagnostics;

/// <summary>
///     The client's half of <c>ks_shotdiag</c>, when the server says to (the command is the server's, and an admin's): finds who the cursor is on, gathers what this client has about them, asks
///         the server for what it has, and prints the two side by side with the differences marked.
/// </summary>
public sealed partial class KsShotDiagnosticsSystem : KsSharedShotDiagnosticsSystem
{
    [Dependency] private IConsoleHost _consoleHost = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private Robust.Shared.Timing.IGameTiming _gameTiming = default!;
    [Dependency] private IInputManager _inputManager = default!;
    [Dependency] private IStateManager _stateManager = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private KsPendingDamageSystem _pendingDamageSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    /// <summary>
    ///     How far from the cursor a mob is looked for, when the cursor is on none.
    /// </summary>
    private const float NearestMobRange = 2f;

    /// <summary>
    ///     What this client had about each entity asked about, at the moment it asked, until the server answers. A
    ///         transient work list, emptied by the answers.
    /// </summary>
    private readonly Dictionary<NetEntity, (List<string> Keys, List<string> Values)> _awaiting = new();

    [SubscribeNetworkEvent]
    private void OnStart(KsShotDiagnosticsStartEvent ev)
    {
        if (ev.Target is { } givenNetEntity)
        {
            if (TryGetEntity(givenNetEntity, out var givenUid))
                Diagnose(givenUid.Value, clickedUid: null, clickLookedUp: false);
            else
                _consoleHost.WriteError(null, Loc.GetString("ks-shotdiag-command-bad-target", ("target", givenNetEntity.ToString())));

            return;
        }

        if (FindTarget(out var clickedUid) is { } targetUid)
            Diagnose(targetUid, clickedUid, clickLookedUp: true);
        else
            _consoleHost.WriteError(null, Loc.GetString("ks-shotdiag-command-no-target"));
    }

    /// <summary>
    ///     Whoever the cursor is on, as a shot fired now would take it, or failing that the mob nearest the cursor.
    /// </summary>
    /// <param name="clickedUid">What a click here resolves to: what a shot fired now would be aimed at.</param>
    private EntityUid? FindTarget(out EntityUid? clickedUid)
    {
        clickedUid = null;

        var cursorCoordinates = _eyeManager.PixelToMap(_inputManager.MouseScreenPosition);
        if (cursorCoordinates.MapId == MapId.Nullspace)
            return null;

        if (_stateManager.CurrentState is GameplayStateBase gameplayState)
            clickedUid = gameplayState.GetClickedEntity(cursorCoordinates);

        if (clickedUid is { } clicked && HasComp<MobStateComponent>(clicked))
            return clicked;

        EntityUid? nearestUid = null;
        var nearestDistance = float.MaxValue;
        foreach (var mob in _entityLookupSystem.GetEntitiesInRange<MobStateComponent>(cursorCoordinates, NearestMobRange))
        {
            var distance = Vector2.Distance(_transformSystem.GetWorldPosition(mob.Owner), cursorCoordinates.Position);
            if (distance >= nearestDistance)
                continue;

            nearestUid = mob.Owner;
            nearestDistance = distance;
        }

        return nearestUid ?? clickedUid;
    }

    /// <summary>
    ///     Gathers what this client has about <paramref name="targetUid"/> and asks the server for its half. Printed when
    ///         the answer arrives.
    /// </summary>
    /// <param name="clickedUid">What a click at the cursor resolves to, if that was looked up.</param>
    private void Diagnose(EntityUid targetUid, EntityUid? clickedUid, bool clickLookedUp)
    {
        var keys = new List<string>();
        var values = new List<string>();
        CollectShared(targetUid, keys, values);

        // What only this client has.
        keys.Add("click at the cursor is on it");
        values.Add(clickLookedUp ? (clickedUid == targetUid).ToString() : NoValue);

        keys.Add("click at the cursor resolves to");
        values.Add(clickLookedUp ? (clickedUid is { } clicked ? ToPrettyString(clicked).ToString() : "nothing") : NoValue);

        keys.Add("your shots pass it (as told by the server)");
        values.Add(TryComp<NpcFactionMemberComponent>(targetUid, out var factionMemberComponent)
            ? (factionMemberComponent.KsLocalPlayerShotsPass ? bool.TrueString : bool.FalseString)
            : NoValue);

        keys.Add("pending damage");
        values.Add(_pendingDamageSystem.GetPendingDamage(targetUid).ToString());

        var targetNetEntity = GetNetEntity(targetUid);
        _awaiting[targetNetEntity] = (keys, values);
        RaiseNetworkEvent(new KsShotDiagnosticsRequestEvent(targetNetEntity));
    }

    [SubscribeNetworkEvent]
    private void OnResponse(KsShotDiagnosticsResponseEvent ev)
    {
        if (!_awaiting.Remove(ev.Target, out var client))
            return;

        var rows = new List<(string Key, string Client, string Server)>();
        for (var i = 0; i < client.Keys.Count; i++)
        {
            rows.Add((client.Keys[i], client.Values[i], NoValue));
        }

        // The server's values go beside the client's of the same key, and what only it has goes after.
        for (var i = 0; i < ev.Keys.Length && i < ev.Values.Length; i++)
        {
            var index = rows.FindIndex(row => row.Key == ev.Keys[i]);
            if (index >= 0)
                rows[index] = rows[index] with { Server = ev.Values[i] };
            else
                rows.Add((ev.Keys[i], NoValue, ev.Values[i]));
        }

        var keyWidth = rows.Max(row => row.Key.Length);
        var clientWidth = Math.Max("client".Length, rows.Max(row => row.Client.Length));

        var builder = new StringBuilder();
        builder.AppendLine(Loc.GetString("ks-shotdiag-command-header",
            ("entity", TryGetEntity(ev.Target, out var targetUid) ? ToPrettyString(targetUid.Value).ToString() : ev.Target.ToString()),
            ("tick", _gameTiming.CurTick.Value.ToString(CultureInfo.InvariantCulture))));

        if (ev.Refused)
            builder.AppendLine(Loc.GetString("ks-shotdiag-command-refused"));

        builder.AppendLine($"  {"".PadRight(keyWidth)}  {"client".PadRight(clientWidth)}  server");

        var differences = 0;
        foreach (var (key, clientValue, serverValue) in rows)
        {
            var differs = !ev.Refused &&
                          clientValue != NoValue &&
                          serverValue != NoValue &&
                          clientValue != serverValue &&
                          !ApproximateKeys.Contains(key);
            if (differs)
                differences++;

            builder.AppendLine($"{(differs ? "!" : " ")} {key.PadRight(keyWidth)}  {clientValue.PadRight(clientWidth)}  {(ev.Refused ? "?" : serverValue)}");
        }

        if (!ev.Refused)
            builder.Append(Loc.GetString("ks-shotdiag-command-differences", ("count", differences)));

        _consoleHost.WriteLine(null, builder.ToString().TrimEnd());
    }
}
