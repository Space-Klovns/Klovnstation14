using Content.Shared._Goobstation.Silicon.Bots;
using Content.Server.Chat.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Emag.Components;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Content.Shared.Repairable;
using System.Linq;

namespace Content.Server._Goobstation.NPC.HTN.PrimitiveTasks.Operators.Specific;

public sealed partial class WeldbotWeldOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    private ChatSystem _chat = default!;
    private WeldbotSystem _weldbot = default!;
    private SharedAudioSystem _audio = default!;
    private SharedInteractionSystem _interaction = default!;
    private SharedPopupSystem _popup = default!;
    private DamageableSystem _damageableSystem = default!;
    private TagSystem _tagSystem = default!;

    /// <summary>
    /// Target entity to inject.
    /// </summary>
    [DataField(required: true)]
    public string TargetKey = string.Empty;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _chat = sysManager.GetEntitySystem<ChatSystem>();
        _weldbot = sysManager.GetEntitySystem<WeldbotSystem>();
        _audio = sysManager.GetEntitySystem<SharedAudioSystem>();
        _interaction = sysManager.GetEntitySystem<SharedInteractionSystem>();
        _popup = sysManager.GetEntitySystem<SharedPopupSystem>();
        _damageableSystem = sysManager.GetEntitySystem<DamageableSystem>();
        _tagSystem = sysManager.GetEntitySystem<TagSystem>();
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        blackboard.Remove<EntityUid>(TargetKey);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entMan) || _entMan.Deleted(target))
            return HTNOperatorStatus.Failed;

        if (!_entMan.TryGetComponent<RepairableComponent>(target, out var repairComp)
            || !_entMan.TryGetComponent<WeldbotComponent>(owner, out var botComp)
            || !_entMan.TryGetComponent<DamageableComponent>(target, out var damage)
            || !_interaction.InRangeUnobstructed(owner, target))
            return HTNOperatorStatus.Failed;

        var existingDamage = _damageableSystem.GetDamagePerGroup((target, damage));
        var botDamage = _weldbot.GetDamageAmount(botComp);
        var damageGroups = _weldbot.GetDamageAmountGroups(botComp, _prototypeManager);
        if (existingDamage.Keys.Intersect(damageGroups.Keys).All(key => existingDamage.TryGetValue(key, out var value) ? value == 0 : true)
            && !_entMan.HasComponent<EmaggedComponent>(owner))
            return HTNOperatorStatus.Failed;

        if (botComp.IsEmagged)
        {
            _damageableSystem.TryChangeDamage((target, damage), -botDamage, true, false);
        }
        else
        {
            _damageableSystem.TryChangeDamage((target, damage), botDamage, true, false);
        }

        _audio.PlayPvs(botComp.WeldSound, target);

        var postDamage = _damageableSystem.GetDamagePerGroup((target, damage));
        if (postDamage.Keys.Intersect(damageGroups.Keys).All(key => postDamage.TryGetValue(key, out var value) ? value == 0 : true)) //only say "all done if we're actually done!"
            _chat.TrySendInGameICMessage(owner, Loc.GetString("weldbot-finish-weld"), InGameICChatType.Speak, hideChat: true, hideLog: true);

        return HTNOperatorStatus.Finished;
    }
}
