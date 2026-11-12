using System.Numerics;
using Content.Shared._KS14.FieldCommander;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
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
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EntityQuery<KsFieldCommanderFogComponent> _fogQuery = default!;

    private TimeSpan _nextCheck;

    /// <summary>
    ///     Puts a fog of war on the map: everything further than <paramref name="range"/> from
    ///         <paramref name="origin"/> (map-local) is fogged, and hurts members of <paramref name="faction"/>.
    /// </summary>
    public void SetFog(EntityUid mapUid, Vector2 origin, float range, ProtoId<ScenarioFactionPrototype> faction)
    {
        var fogComponent = EnsureComp<KsFieldCommanderFogComponent>(mapUid);
        fogComponent.Origin = origin;
        fogComponent.Range = range;
        fogComponent.Faction = faction;
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
        while (query.MoveNext(out var uid, out var memberComponent, out var mobStateComponent, out var transformComponent))
        {
            if (mobStateComponent.CurrentState == MobState.Dead ||
                transformComponent.MapUid is not { } mapUid ||
                !_fogQuery.TryComp(mapUid, out var fogComponent) ||
                memberComponent.Id != fogComponent.Faction)
                continue;

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

            _popupSystem.PopupEntity(
                KsPopupMessage.Create(outside ? "ks-fieldcommander-fog-choking" : "ks-fieldcommander-fog-warning"),
                uid, uid, type: outside ? PopupType.LargeCaution : PopupType.MediumCaution);
        }
    }
}
