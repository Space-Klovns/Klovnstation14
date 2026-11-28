using Content.Server._KS14.Cultivation.Components;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Body;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Cultivation.Systems;

// Corpse Puppet: a human corpse strung up with demonic qi, hopping after its master as a jiangshi. The real body rides
// inside the puppet and drops out again when the qi runs out or the puppet is cut down.
public sealed partial class ForbiddenArtsSystem
{
    private static readonly ProtoId<HTNCompoundPrototype> PuppetCompound = "KsSpiritBeastCompound";

    [Dependency] private MetaDataSystem _metaDataSystem = default!;
    [Dependency] private NPCSystem _npcSystem = default!;
    [Dependency] private NPCRetaliationSystem _npcRetaliationSystem = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedVisualBodySystem _visualBodySystem = default!;

    [SubscribeLocalEvent]
    private void OnCorpsePuppet(Entity<CultivatorComponent> entity, ref CorpsePuppetActionEvent args)
    {
        if (args.Handled)
            return;

        var corpseUid = args.Target;
        if (!_mobStateSystem.IsDead(corpseUid) || !HasComp<HumanoidProfileComponent>(corpseUid) || HasComp<JiangshiComponent>(corpseUid))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-puppet-unfit", ("target", corpseUid)));
            return;
        }

        if (!_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtMistShadow, args.Target);
        _visualsSystem.Art(CultivationVisualsSystem.ArtVoidMotes, args.Target);
        _visualsSystem.Strike(entity, args.Target, FxColour.Blood);

        // One puppet at a time: the old one collapses.
        var masterComponent = EnsureComp<JiangshiMasterComponent>(entity);
        if (masterComponent.Puppet is { } oldPuppetUid && !TerminatingOrDeleted(oldPuppetUid) && !EntityManager.IsQueuedForDeletion(oldPuppetUid))
            CollapsePuppet(oldPuppetUid);

        var puppetUid = Spawn(args.Puppet, Transform(corpseUid).Coordinates);
        _visualBodySystem.CopyAppearanceFrom(corpseUid, puppetUid);
        _metaDataSystem.SetEntityName(puppetUid, Loc.GetString("cultivation-jiangshi-name", ("corpse", Name(corpseUid))));

        var corpseContainer = _containerSystem.EnsureContainer<ContainerSlot>(puppetUid, JiangshiComponent.CorpseContainer);
        _containerSystem.Insert(corpseUid, corpseContainer, force: true);

        var lifetime = args.BaseLifetime + TimeSpan.FromMinutes((double)Math.Max(0, (int)entity.Comp.Realm - (int)CultivationRealm.GoldenCore));
        var jiangshiComponent = EnsureComp<JiangshiComponent>(puppetUid);
        jiangshiComponent.Master = entity;
        jiangshiComponent.ExpiresAt = _gameTiming.CurTime + lifetime;
        masterComponent.Puppet = puppetUid;

        // It takes its master's side and hops after them.
        _npcFactionSystem.ClearFactions(puppetUid);
        if (TryComp<NpcFactionMemberComponent>(entity, out var masterFactions) && masterFactions.Factions.Count > 0)
            _npcFactionSystem.AddFactions(puppetUid, new HashSet<ProtoId<NpcFactionPrototype>>(masterFactions.Factions));

        if (TryComp<HTNComponent>(puppetUid, out var htnComponent))
        {
            htnComponent.RootTask = new HTNCompoundTask { Task = PuppetCompound };
            _npcSystem.SetBlackboard(puppetUid, NPCBlackboard.FollowTarget, new EntityCoordinates(entity, System.Numerics.Vector2.Zero), htnComponent);
            _npcSystem.WakeNPC(puppetUid, htnComponent);
        }

        AddTaint(entity, 5f);
        _noticeSystem.Announce(puppetUid, Loc.GetString("cultivation-puppet-rises", ("user", entity.Owner), ("target", puppetUid)), type: PopupType.LargeCaution);
        _adminLogger.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(entity):player} raised the corpse of {ToPrettyString(corpseUid):target} as a jiangshi");
    }

    private void UpdatePuppets(TimeSpan now)
    {
        var puppetQuery = EntityQueryEnumerator<JiangshiComponent>();
        while (puppetQuery.MoveNext(out var puppetUid, out var jiangshiComponent))
        {
            if (now >= jiangshiComponent.ExpiresAt && !EntityManager.IsQueuedForDeletion(puppetUid))
                CollapsePuppet(puppetUid);
        }
    }

    /// <summary>
    ///     The qi runs out, and the body drops where the puppet stood.
    /// </summary>
    public void CollapsePuppet(EntityUid puppetUid)
    {
        if (TerminatingOrDeleted(puppetUid) || EntityManager.IsQueuedForDeletion(puppetUid))
            return;

        DropCorpse(puppetUid);
        _noticeSystem.Announce(puppetUid, Loc.GetString("cultivation-puppet-collapses", ("target", puppetUid)), type: PopupType.Medium);
        QueueDel(puppetUid);
    }

    private void DropCorpse(EntityUid puppetUid)
    {
        if (_containerSystem.TryGetContainer(puppetUid, JiangshiComponent.CorpseContainer, out var corpseContainer))
            _containerSystem.EmptyContainer(corpseContainer, force: true);
    }

    /// <summary>
    ///     Cut down, it collapses. Dropping the body moves it out of a container, which is no work for the middle of
    ///         a mob state change, so the collapse waits for Update.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPuppetMobState(Entity<JiangshiComponent> entity, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            entity.Comp.ExpiresAt = _gameTiming.CurTime;
    }

    /// <summary>
    ///     However the puppet goes, the body it carried is never lost with it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPuppetTerminating(Entity<JiangshiComponent> entity, ref EntityTerminatingEvent args)
    {
        DropCorpse(entity);
    }

    /// <summary>
    ///     Whoever hurts the master answers to the puppet.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPuppetMasterDamaged(Entity<JiangshiMasterComponent> entity, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.Origin is not { } attackerUid || attackerUid == entity.Comp.Puppet
            || entity.Comp.Puppet is not { } puppetUid || !TryComp<NPCRetaliationComponent>(puppetUid, out var retaliationComponent))
            return;

        _npcRetaliationSystem.TryRetaliate((puppetUid, retaliationComponent), attackerUid);
    }

    /// <summary>
    ///     The master strikes someone: their puppet goes for them too (not for the master's friends).
    /// </summary>
    public void MasterStruck(EntityUid masterUid, IReadOnlyList<EntityUid> struck)
    {
        if (!TryComp<JiangshiMasterComponent>(masterUid, out var masterComponent) || masterComponent.Puppet is not { } puppetUid
            || TerminatingOrDeleted(puppetUid) || !TryComp<NPCRetaliationComponent>(puppetUid, out var retaliationComponent))
            return;

        foreach (var targetUid in struck)
        {
            if (targetUid == puppetUid || targetUid == masterUid || _sharedCultivationSystem.IsAlly(masterUid, targetUid))
                continue;

            _npcRetaliationSystem.TryRetaliate((puppetUid, retaliationComponent), targetUid);
        }
    }

    /// <summary>
    ///     A sealing talisman pressed to a jiangshi's brow drops it at once.
    /// </summary>
    public bool TrySealPuppet(EntityUid puppetUid)
    {
        if (!HasComp<JiangshiComponent>(puppetUid))
            return false;

        CollapsePuppet(puppetUid);
        return true;
    }
}
