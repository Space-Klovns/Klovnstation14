using Content.Server.NPC.Components;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Collections;
using Robust.Shared.Timing;

namespace Content.Server.NPC.Systems;

/// <summary>
///     Handles NPC which become aggressive after being attacked.
/// </summary>
public sealed class NPCRetaliationSystem : EntitySystem
{
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly _KS14.NPC.Systems.NpcRetaliationWarningSystem _retaliationWarningSystem = default!; // KS14

    /// <inheritdoc />
    public override void Initialize()
    {
        SubscribeLocalEvent<NPCRetaliationComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<NPCRetaliationComponent, DisarmedEvent>(OnDisarmed);

        SubscribeLocalEvent<NPCRetaliationComponent, ContactInteractionEvent>(KsOnDisarmed); // KS14: ANK: contact should warrant retaliation
    }

    private void OnDamageChanged(Entity<NPCRetaliationComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased)
            return;

        if (args.Origin is not { } origin)
            return;

        TryRetaliate(ent, origin);
    }

    private void OnDisarmed(Entity<NPCRetaliationComponent> ent, ref DisarmedEvent args)
    {
        TryRetaliate(ent, args.Source);
    }

    // KS14: ANK
    private void KsOnDisarmed(Entity<NPCRetaliationComponent> ent, ref ContactInteractionEvent args)
    {
        TryRetaliate(ent, args.Other, tryWarn: true);
    }

    public bool TryRetaliate(Entity<NPCRetaliationComponent> ent, EntityUid target, bool tryWarn = false /* KS14 */)
    {
        // don't retaliate against inanimate objects.
        if (!HasComp<MobStateComponent>(target))
            return false;

        // don't retaliate against the same faction
        if (_npcFaction.IsEntityFriendly(ent.Owner, target))
            return false;

        // KS14 Start
        if (tryWarn &&
            ent.Comp.WarnDuration is { } warnDuration &&
            !ent.Comp.AttackMemories.ContainsKey(target) &&
            _retaliationWarningSystem.TryWarn(ent.Owner, target, warnDuration))
        {
            return false;
        }
        // KS14 End

        _npcFaction.AggroEntity(ent.Owner, target);
        if (ent.Comp.AttackMemoryLength is { } memoryLength)
            ent.Comp.AttackMemories[target] = _timing.CurTime + memoryLength;

        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NPCRetaliationComponent, FactionExceptionComponent>();
        while (query.MoveNext(out var uid, out var retaliationComponent, out var factionException))
        {
            // TODO: can probably reuse this allocation and clear it
            foreach (var entity in new ValueList<EntityUid>(retaliationComponent.AttackMemories.Keys))
            {
                if (!TerminatingOrDeleted(entity) && _timing.CurTime < retaliationComponent.AttackMemories[entity])
                    continue;

                _npcFaction.DeAggroEntity((uid, factionException), entity);
                // TODO: should probably remove the AttackMemory, thats the whole point of the ValueList right??
            }
        }
    }
}
