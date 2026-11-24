using System.Globalization;
using Content.Server.Administration.Managers;
using Content.Server.Movement.Components;
using Content.Shared._KS14.FactionCollision;
using Content.Shared._KS14.Projectiles.Diagnostics;
using Content.Shared.Administration;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Player;

namespace Content.Server._KS14.Projectiles.Diagnostics;

/// <summary>
///     Answers a client's <c>ks_shotdiag</c> with what the server has about an entity.
/// </summary>
public sealed partial class KsShotDiagnosticsSystem : KsSharedShotDiagnosticsSystem
{
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;

    [SubscribeNetworkEvent]
    private void OnRequest(KsShotDiagnosticsRequestEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!_adminManager.HasAdminFlag(session, AdminFlags.Debug) ||
            !TryGetEntity(ev.Target, out var targetUid))
        {
            RaiseNetworkEvent(new KsShotDiagnosticsResponseEvent(ev.Target, refused: true, [], []), session);
            return;
        }

        var keys = new List<string>();
        var values = new List<string>();
        CollectShared(targetUid.Value, keys, values);

        // What only the server has.
        keys.Add("your shots pass it (same side)");
        values.Add(session.AttachedEntity is { } shooterUid ? ShotsPass(shooterUid, targetUid.Value).ToString() : NoValue);

        keys.Add("lag compensation positions");
        values.Add(TryComp<LagCompensationComponent>(targetUid, out var lagCompensationComponent)
            ? lagCompensationComponent.Positions.Count.ToString(CultureInfo.InvariantCulture)
            : NoValue);

        keys.Add("your ping");
        values.Add(session.Ping.ToString(CultureInfo.InvariantCulture));

        RaiseNetworkEvent(new KsShotDiagnosticsResponseEvent(ev.Target, refused: false, keys.ToArray(), values.ToArray()), session);
    }

    /// <summary>
    ///     Whether <paramref name="shooterUid"/>'s shots pass through <paramref name="targetUid"/> for being on its side,
    ///         as <see cref="KsFactionCollisionSystem"/> has it.
    /// </summary>
    private bool ShotsPass(EntityUid shooterUid, EntityUid targetUid)
    {
        return shooterUid != targetUid &&
               HasComp<KsFactionCollisionShooterComponent>(shooterUid) &&
               TryComp<NpcFactionMemberComponent>(shooterUid, out var shooterFactionComponent) &&
               _npcFactionSystem.IsMemberOfAny(targetUid, shooterFactionComponent.Factions);
    }
}
