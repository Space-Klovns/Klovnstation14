using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Perks.Support;

/// <summary>
///     A marked target (<see cref="KsMarkedTargetComponent"/>) takes more damage, from anything; and who sees it marked.
/// </summary>
public sealed partial class KsMarkedTargetSystem : EntitySystem
{
    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionMemberQuery = default!;

    /// <summary>
    ///     Marks <paramref name="targetUid"/> for <paramref name="markerUid"/>'s side until <paramref name="endTime"/>.
    ///         Marked again, it keeps the later end.
    /// </summary>
    public KsMarkedTargetComponent Mark(EntityUid targetUid, EntityUid markerUid, TimeSpan endTime, float damageTakenMultiplier, Color outlineColor)
    {
        var markedComponent = EnsureComp<KsMarkedTargetComponent>(targetUid);
        markedComponent.EndTime = markedComponent.EndTime > endTime ? markedComponent.EndTime : endTime;
        markedComponent.DamageTakenMultiplier = damageTakenMultiplier;
        markedComponent.OutlineColor = outlineColor;
        markedComponent.MarkerUid = markerUid;
        markedComponent.Factions = _factionMemberQuery.TryComp(markerUid, out var factionMemberComponent)
            ? new HashSet<ProtoId<NpcFactionPrototype>>(factionMemberComponent.Factions)
            : new HashSet<ProtoId<NpcFactionPrototype>>();
        Dirty(targetUid, markedComponent);
        return markedComponent;
    }

    /// <summary>
    ///     Whether <paramref name="viewerUid"/> sees the mark: the marker, or anyone on its side.
    /// </summary>
    public bool CanSeeMark(KsMarkedTargetComponent markedComponent, EntityUid viewerUid)
    {
        return viewerUid == markedComponent.MarkerUid ||
               _factionMemberQuery.TryComp(viewerUid, out var factionMemberComponent) &&
               markedComponent.Factions.Overlaps(factionMemberComponent.Factions);
    }

    // A by-value event, so no Entity<T> handler (RA0054).
    [SubscribeLocalEvent]
    private void OnDamageModify(EntityUid uid, KsMarkedTargetComponent component, DamageModifyEvent args)
    {
        if (component.DamageTakenMultiplier == 1f)
            return;

        // Harm only: healing a marked target isn't made stronger.
        var damage = new DamageSpecifier(args.Damage);
        foreach (var (type, amount) in args.Damage.DamageDict)
        {
            if (amount > FixedPoint2.Zero)
                damage.DamageDict[type] = amount * component.DamageTakenMultiplier;
        }

        args.Damage = damage;
    }
}
