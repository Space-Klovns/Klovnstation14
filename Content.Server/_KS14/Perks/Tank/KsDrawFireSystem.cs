using Content.Server._KS14.NPC.Targeting;
using Content.Shared._KS14.Perks.Tank;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Perks.Tank;

/// <summary>
///     Draw Fire (<see cref="KsDrawFireAbilityComponent"/>): for a while the holder is the target enemy NPCs prefer.
///         Whatever priority the holder had is put back afterwards.
/// </summary>
public sealed partial class KsDrawFireSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private EntityQuery<KsNpcTargetPriorityComponent> _priorityQuery = default!;

    [SubscribeLocalEvent]
    private void OnAction(Entity<KsDrawFireAbilityComponent> entity, ref KsDrawFireActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        // Taunting again only makes it last longer.
        if (entity.Comp.ActiveUntil == null)
        {
            if (_priorityQuery.TryComp(entity.Owner, out var priorityComponent))
                entity.Comp.PreviousPriority = priorityComponent.Priority;
            else
            {
                entity.Comp.PreviousPriority = null;
                priorityComponent = AddComp<KsNpcTargetPriorityComponent>(entity.Owner);
            }

            priorityComponent.Priority = MathF.Max(entity.Comp.Priority, entity.Comp.PreviousPriority ?? 0f);
        }

        entity.Comp.ActiveUntil = _gameTiming.CurTime + entity.Comp.Duration;
        Dirty(entity);

        _audioSystem.PlayPvs(entity.Comp.Sound, entity.Owner);
        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-perk-tank-draw-fire", ("user", entity.Owner)), entity.Owner, PopupType.LargeCaution);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        var query = EntityQueryEnumerator<KsDrawFireAbilityComponent>();
        while (query.MoveNext(out var uid, out var drawFireComponent))
        {
            if (drawFireComponent.ActiveUntil is { } activeUntil && activeUntil <= curTime)
                End((uid, drawFireComponent), popup: true);
        }
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsDrawFireAbilityComponent> entity, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(entity.Owner))
            End(entity, popup: false);
    }

    private void End(Entity<KsDrawFireAbilityComponent> entity, bool popup)
    {
        if (entity.Comp.ActiveUntil == null)
            return;

        entity.Comp.ActiveUntil = null;
        Dirty(entity);

        if (entity.Comp.PreviousPriority is { } previousPriority)
        {
            if (_priorityQuery.TryComp(entity.Owner, out var priorityComponent))
                priorityComponent.Priority = previousPriority;
        }
        else
        {
            RemComp<KsNpcTargetPriorityComponent>(entity.Owner);
        }

        entity.Comp.PreviousPriority = null;

        if (popup)
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-perk-tank-draw-fire-ended"), entity.Owner, entity.Owner);
    }
}
