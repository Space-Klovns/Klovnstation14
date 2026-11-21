using Content.Server._KS14.Cultivation.Components;
using Content.Server.Instruments;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Examine;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     The guqin, the scholar's zither. Playing it is cultivation: while someone plays, they and every cultivator
///         listening nearby slowly gain insight and their qi settles, and everyone else just enjoys the music. A
///         cultivator plays it better the higher their realm.
/// </summary>
public sealed partial class GuqinSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private SharedCultivationSystem _sharedCultivationSystem = default!;
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;

    private readonly HashSet<Entity<MobStateComponent>> _listeners = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<GuqinComponent, InstrumentComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var guqinComponent, out var instrumentComponent, out var transformComponent))
        {
            var playerUid = transformComponent.ParentUid;
            if (!instrumentComponent.Playing || !HasComp<MobStateComponent>(playerUid) || !_mobStateSystem.IsAlive(playerUid))
            {
                guqinComponent.PlayTime = 0f;
                continue;
            }

            guqinComponent.PlayTime += frameTime;
            if (guqinComponent.PlayTime < guqinComponent.PulseInterval)
                continue;

            guqinComponent.PlayTime = 0f;
            Resonate((uid, guqinComponent), playerUid);
        }
    }

    /// <summary>
    ///     One wave of music washing over the room.
    /// </summary>
    public void Resonate(Entity<GuqinComponent> entity, EntityUid playerUid)
    {
        var now = _gameTiming.CurTime;
        var playerCultivator = CompOrNull<CultivatorComponent>(playerUid);
        var power = playerCultivator != null ? 1f + 0.25f * (float)playerCultivator.Realm : 0.5f;

        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtPetals, playerUid);
        _visualsSystem.Wave(playerUid, FxColour.Water);

        if (playerCultivator != null)
        {
            _sharedCultivationSystem.AddInsight((playerUid, playerCultivator), entity.Comp.Insight * power);
            entity.Comp.Pulses++;
            if (entity.Comp.Pulses % entity.Comp.PulsesPerInjuryHeal == 0 && _sharedCultivationSystem.HealInternalInjury((playerUid, playerCultivator)))
                _noticeSystem.Notify(playerUid, Loc.GetString("guqin-meridians-settle"));
        }

        _listeners.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(playerUid).Coordinates, entity.Comp.Range, _listeners);
        foreach (var listener in _listeners)
        {
            var listenerUid = listener.Owner;
            if (listenerUid == playerUid || !_mobStateSystem.IsAlive(listenerUid))
                continue;

            if (!TryComp<CultivatorComponent>(listenerUid, out var listenerCultivator))
            {
                _noticeSystem.Notify(listenerUid, Loc.GetString("guqin-peace"));
                continue;
            }

            if (entity.Comp.ListenedAt.TryGetValue(listenerUid, out var listenedAt) && now < listenedAt + entity.Comp.ListenerCooldown)
                continue;

            entity.Comp.ListenedAt[listenerUid] = now;
            _sharedCultivationSystem.AddInsight((listenerUid, listenerCultivator), entity.Comp.Insight * power);
            _noticeSystem.Notify(listenerUid, Loc.GetString("guqin-insight"), PopupType.Small);
        }
    }

    [SubscribeLocalEvent]
    private void OnGuqinExamined(Entity<GuqinComponent> entity, ref ExaminedEvent args)
    {
        if (HasComp<CultivatorComponent>(args.Examiner))
            args.PushMarkup(Loc.GetString("guqin-examine-cultivator"));
    }
}
