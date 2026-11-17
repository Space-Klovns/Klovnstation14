using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Prototypes;
using Content.Shared.Actions;
using Content.Shared.Damage.Events;
using Content.Shared.Examine;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Cultivation.Systems;

public enum MartialArtLearnResult : byte
{
    Learned,
    AlreadyKnown,
    NotCultivator,
    RealmTooLow,
    NoRoom,
}

/// <summary>
///     Martial arts learned from manuals. Body arts are open to anyone, up to <see cref="MartialArtistComponent.MaxBodyArts"/>.
///         Qi arts need a cultivator, who can hold one, plus one more per realm above Qi Condensation.
/// </summary>
public sealed partial class SharedMartialArtsSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private EntityQuery<CultivatorComponent> _cultivatorQuery = default!;
    [Dependency] private EntityQuery<MartialArtistComponent> _martialArtistQuery = default!;

    /// <summary>
    ///     Qi arts a cultivator can hold at a realm.
    /// </summary>
    public static int QiArtSlots(CultivationRealm realm)
    {
        return 1 + (int)realm;
    }

    public bool KnowsArt(EntityUid uid, ProtoId<MartialArtPrototype> art)
    {
        return _martialArtistQuery.TryComp(uid, out var artistComponent) && artistComponent.Arts.Contains(art);
    }

    public int CountArts(EntityUid uid, MartialArtKind kind)
    {
        if (!_martialArtistQuery.TryComp(uid, out var artistComponent))
            return 0;

        var count = 0;
        foreach (var artId in artistComponent.Arts)
        {
            if (ProtoMan.TryIndex(artId, out var art) && art.Kind == kind)
                count++;
        }

        return count;
    }

    public MartialArtLearnResult CanLearn(EntityUid uid, MartialArtPrototype art)
    {
        if (KnowsArt(uid, art.ID))
            return MartialArtLearnResult.AlreadyKnown;

        if (art.Kind == MartialArtKind.Qi)
        {
            if (!_cultivatorQuery.TryComp(uid, out var cultivatorComponent))
                return MartialArtLearnResult.NotCultivator;

            if (cultivatorComponent.Realm < art.MinRealm)
                return MartialArtLearnResult.RealmTooLow;

            return CountArts(uid, MartialArtKind.Qi) < QiArtSlots(cultivatorComponent.Realm)
                ? MartialArtLearnResult.Learned
                : MartialArtLearnResult.NoRoom;
        }

        var maxBodyArts = _martialArtistQuery.TryComp(uid, out var artistComponent)
            ? artistComponent.MaxBodyArts
            : new MartialArtistComponent().MaxBodyArts;

        return CountArts(uid, MartialArtKind.Body) < maxBodyArts
            ? MartialArtLearnResult.Learned
            : MartialArtLearnResult.NoRoom;
    }

    /// <summary>
    ///     Learn an art if allowed: its actions are granted and its passive changes applied.
    /// </summary>
    public MartialArtLearnResult TryLearn(EntityUid uid, ProtoId<MartialArtPrototype> artId)
    {
        if (!ProtoMan.TryIndex(artId, out var art))
            return MartialArtLearnResult.NoRoom;

        var result = CanLearn(uid, art);
        if (result != MartialArtLearnResult.Learned)
            return result;

        var artistComponent = EnsureComp<MartialArtistComponent>(uid);
        artistComponent.Arts.Add(artId);

        foreach (var actionProto in art.Actions)
        {
            if (artistComponent.Actions.ContainsKey(actionProto))
                continue;

            EntityUid? actionUid = null;
            if (_actionsSystem.AddAction(uid, ref actionUid, actionProto))
            {
                artistComponent.Actions[actionProto] = actionUid.Value;
                var learnedEvent = new CultivationTechniqueLearnedEvent(actionUid.Value);
                RaiseLocalEvent(uid, ref learnedEvent);
            }
        }

        EntityManager.AddComponents(uid, art.Components, removeExisting: false);

        if (art.Forbidden)
            EnsureComp<DarkArtsComponent>(uid);

        return MartialArtLearnResult.Learned;
    }

    public bool HasArtAction(EntityUid uid, EntProtoId actionProto)
    {
        return _martialArtistQuery.TryComp(uid, out var artistComponent) && artistComponent.Actions.ContainsKey(actionProto);
    }

    [SubscribeLocalEvent]
    private void OnDarkArtsExamined(Entity<DarkArtsComponent> entity, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("martial-art-dark-arts-examine", ("target", entity.Owner)));
    }

    [SubscribeLocalEvent]
    private void OnMuscleTendonStamina(Entity<MuscleTendonComponent> entity, ref BeforeStaminaDamageEvent args)
    {
        if (args.Value > 0)
            args.Value *= entity.Comp.StaminaDamageMultiplier;
    }
}
