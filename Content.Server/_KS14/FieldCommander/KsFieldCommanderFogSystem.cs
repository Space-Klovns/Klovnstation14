using System.Numerics;
using Content.Shared._KS14.FieldCommander;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     Hurts defenders who walk out of the play area into the fog, and warns them on the way.
/// </summary>
public sealed partial class KsFieldCommanderFogSystem : EntitySystem
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PopupInterval = TimeSpan.FromSeconds(4);

    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EntityQuery<KsFieldCommanderFogComponent> _fogQuery = default!;

    private TimeSpan _nextCheck;

    /// <summary>
    ///     Puts a fog of war on the map: everything further than <paramref name="range"/> from
    ///         <paramref name="origin"/> (map-local) is fogged.
    /// </summary>
    public void SetFog(EntityUid mapUid, Vector2 origin, float range)
    {
        var fogComponent = EnsureComp<KsFieldCommanderFogComponent>(mapUid);
        fogComponent.Origin = origin;
        fogComponent.Range = range;
        Dirty(mapUid, fogComponent);
    }

    public void Clear(EntityUid mapUid)
    {
        RemComp<KsFieldCommanderFogComponent>(mapUid);
    }

    public override void Update(float frameTime)
    {
        if (_gameTiming.CurTime < _nextCheck)
            return;

        _nextCheck = _gameTiming.CurTime + CheckInterval;

        var seconds = (float)CheckInterval.TotalSeconds;
        var query = EntityQueryEnumerator<ScenarioFactionMemberComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var memberComponent, out _, out var transformComponent))
        {
            if (transformComponent.MapUid is not { } mapUid
                || !_fogQuery.TryComp(mapUid, out var fogComponent)
                || memberComponent.Id != fogComponent.Faction
                || _mobStateSystem.IsDead(uid))
            {
                continue;
            }

            var distance = (_transformSystem.GetWorldPosition(transformComponent) - fogComponent.Origin).Length();
            var over = distance - fogComponent.Range;
            if (over < -fogComponent.WarningMargin)
                continue;

            var outside = over > 0f;
            if (outside)
            {
                var scale = MathF.Min(fogComponent.MaxDamageScale, MathF.Pow(2f, over / fogComponent.DoublingDistance));
                _damageableSystem.TryChangeDamage(uid, fogComponent.DamagePerSecond * (scale * seconds), ignoreResistances: true);
            }

            var victimComponent = EnsureComp<KsFieldCommanderFogVictimComponent>(uid);
            if (_gameTiming.CurTime < victimComponent.NextPopup)
                continue;

            victimComponent.NextPopup = _gameTiming.CurTime + PopupInterval;

            if (outside)
            {
                _popupSystem.PopupEntity(
                    KsPopupMessage.Create("ks-fieldcommander-fog-choking"),
                    uid, uid, type: PopupType.LargeCaution);
            }
            else
            {
                _popupSystem.PopupEntity(
                    KsPopupMessage.Create("ks-fieldcommander-fog-warning"),
                    uid, uid, type: PopupType.MediumCaution);
            }
        }
    }
}
