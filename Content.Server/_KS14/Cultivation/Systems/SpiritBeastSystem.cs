using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     A cultivator's spirit beast: a station animal bound by contract. It takes its master's side, follows them,
///         and turns on whoever hurts them. A master holds one contract at a time.
/// </summary>
public sealed partial class SpiritBeastSystem : EntitySystem
{
    private static readonly ProtoId<HTNCompoundPrototype> BeastCompound = "KsSpiritBeastCompound";

    [Dependency] private CultivationSystem _cultivationSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NPCSystem _npcSystem = default!;
    [Dependency] private NPCRetaliationSystem _npcRetaliationSystem = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [SubscribeLocalEvent]
    private void OnBeastContract(Entity<CultivatorComponent> entity, ref BeastContractActionEvent args)
    {
        if (args.Handled)
            return;

        // Used on yourself: call your beast to your side.
        if (args.Target == entity.Owner)
        {
            args.Handled = Summon(entity, args.SummonQiCost, args.SummonRange);
            return;
        }

        var beastUid = args.Target;
        if (!TryComp<HTNComponent>(beastUid, out var htnComponent) || HasComp<HumanoidProfileComponent>(beastUid)
            || _mobStateSystem.IsDead(beastUid))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-beast-unfit", ("target", beastUid)));
            return;
        }

        if (!_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtGoldMotes, args.Target);
        _visualsSystem.Strike(entity, args.Target, _visualsSystem.ColourOf(entity));

        // One contract at a time: the old beast goes free.
        var masterComponent = EnsureComp<SpiritBeastMasterComponent>(entity);
        if (masterComponent.Beast is { } oldBeastUid && oldBeastUid != beastUid)
            RemComp<SpiritBeastComponent>(oldBeastUid);

        masterComponent.Beast = beastUid;
        EnsureComp<SpiritBeastComponent>(beastUid).Master = entity;

        // It takes its master's side...
        _npcFactionSystem.ClearFactions(beastUid);
        if (TryComp<NpcFactionMemberComponent>(entity, out var masterFactions) && masterFactions.Factions.Count > 0)
            _npcFactionSystem.AddFactions(beastUid, new HashSet<ProtoId<NpcFactionPrototype>>(masterFactions.Factions));
        EnsureComp<NPCRetaliationComponent>(beastUid);

        // ...and follows them.
        htnComponent.RootTask = new HTNCompoundTask { Task = BeastCompound };
        _npcSystem.SetBlackboard(beastUid, NPCBlackboard.FollowTarget, new EntityCoordinates(entity, System.Numerics.Vector2.Zero), htnComponent);
        _npcSystem.WakeNPC(beastUid, htnComponent);

        _noticeSystem.Announce(beastUid, Loc.GetString("cultivation-beast-contracted", ("user", entity.Owner), ("target", beastUid)), type: PopupType.Medium);
    }

    private bool Summon(Entity<CultivatorComponent> entity, float qiCost, float range)
    {
        if (!TryComp<SpiritBeastMasterComponent>(entity, out var masterComponent) || masterComponent.Beast is not { } beastUid
            || TerminatingOrDeleted(beastUid) || _mobStateSystem.IsDead(beastUid))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-beast-none"));
            return false;
        }

        if (!_transformSystem.InRange((beastUid, null), (entity.Owner, null), range))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-beast-too-far"));
            return false;
        }

        if (!_cultivationSystem.TryUseTechnique(entity, qiCost))
            return false;

        _transformSystem.SetCoordinates(beastUid, Transform(entity).Coordinates);
        _noticeSystem.Announce(beastUid, Loc.GetString("cultivation-beast-summoned", ("target", beastUid)), type: PopupType.Medium);
        return true;
    }

    /// <summary>
    ///     Whoever hurts the master answers to the beast.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnMasterDamaged(Entity<SpiritBeastMasterComponent> entity, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.Origin is not { } attackerUid || attackerUid == entity.Comp.Beast
            || entity.Comp.Beast is not { } beastUid || !TryComp<NPCRetaliationComponent>(beastUid, out var retaliationComponent))
            return;

        _npcRetaliationSystem.TryRetaliate((beastUid, retaliationComponent), attackerUid);
    }
}

[RegisterComponent, Access(typeof(SpiritBeastSystem))]
public sealed partial class SpiritBeastMasterComponent : Component
{
    [DataField]
    public EntityUid? Beast;
}

[RegisterComponent, Access(typeof(SpiritBeastSystem))]
public sealed partial class SpiritBeastComponent : Component
{
    [DataField]
    public EntityUid? Master;
}
