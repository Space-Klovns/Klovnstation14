using Content.Shared._KS14.Perks.Support;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Perks.Support;

/// <summary>
///     Mark Target (<see cref="KsMarkTargetAbilityComponent"/>): marks an enemy for a while
///         (<see cref="KsMarkedTargetComponent"/>), and takes the mark off when it runs out.
/// </summary>
public sealed partial class KsMarkTargetSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private KsMarkedTargetSystem _markedTargetSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;

    [SubscribeLocalEvent]
    private void OnAction(Entity<KsMarkTargetAbilityComponent> entity, ref KsMarkTargetActionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryMark(entity, args.Target))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-perk-support-mark-invalid"), entity.Owner, entity.Owner);
            return;
        }

        args.Handled = true;
    }

    /// <summary>
    ///     Marks <paramref name="targetUid"/> if it is a living enemy of the marker.
    /// </summary>
    public bool TryMark(Entity<KsMarkTargetAbilityComponent> marker, EntityUid targetUid)
    {
        if (targetUid == marker.Owner ||
            !_mobStateQuery.TryComp(targetUid, out var mobStateComponent) ||
            _mobStateSystem.IsDead(targetUid, mobStateComponent) ||
            _npcFactionSystem.IsEntityFriendly(marker.Owner, targetUid))
            return false;

        _markedTargetSystem.Mark(targetUid, marker.Owner, _gameTiming.CurTime + marker.Comp.Duration,
            marker.Comp.DamageTakenMultiplier, marker.Comp.OutlineColor);

        _audioSystem.PlayEntity(marker.Comp.Sound, marker.Owner, marker.Owner);
        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-perk-support-marked", ("target", targetUid)), targetUid, marker.Owner, PopupType.Medium);
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        var query = EntityQueryEnumerator<KsMarkedTargetComponent>();
        while (query.MoveNext(out var uid, out var markedComponent))
        {
            if (markedComponent.EndTime <= curTime && markedComponent.LifeStage <= ComponentLifeStage.Running)
                RemCompDeferred<KsMarkedTargetComponent>(uid);
        }
    }
}
