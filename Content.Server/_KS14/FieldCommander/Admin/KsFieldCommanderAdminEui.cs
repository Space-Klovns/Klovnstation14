using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._KS14.FieldCommander;
using Content.Shared.Administration;
using Content.Shared.Eui;
using Content.Shared.GameTicking.Components;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.FieldCommander.Admin;

/// <summary>
///     The field commander admin window, server side. Every message is checked against the sender's admin flags and
///         the commander it names, then handed to <see cref="KsFieldCommanderRuleSystem"/>, which checks its values.
/// </summary>
public sealed partial class KsFieldCommanderAdminEui : BaseEui
{
    public const AdminFlags RequiredFlags = AdminFlags.Round;

    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private IEntityManager _entityManager = default!;

    private readonly KsFieldCommanderRuleSystem _ruleSystem;
    private readonly KsFieldCommanderAdminEuiSystem _euiSystem;

    /// <summary>
    ///     The commander shown, kept while it runs so that a second one starting does not swap the window over.
    /// </summary>
    private EntityUid? _ruleUid;

    public KsFieldCommanderAdminEui()
    {
        IoCManager.InjectDependencies(this);
        _ruleSystem = _entityManager.System<KsFieldCommanderRuleSystem>();
        _euiSystem = _entityManager.System<KsFieldCommanderAdminEuiSystem>();
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override void Closed()
    {
        base.Closed();
        _euiSystem.OnClosed(this);
    }

    /// <summary>
    ///     Whether the player it is open for may still use it.
    /// </summary>
    public bool IsAllowed()
    {
        return _adminManager.HasAdminFlag(Player, RequiredFlags);
    }

    public override KsFieldCommanderAdminEuiState GetNewState()
    {
        var state = new KsFieldCommanderAdminEuiState
        {
            DeliveryNames = [..Enum.GetNames<KsFieldCommanderDelivery>()],
            MaxBudget = KsFieldCommanderRuleSystem.MaxEditedBudget,
            MaxCost = KsFieldCommanderRuleSystem.MaxEditedCost,
            MaxWeight = KsFieldCommanderRuleSystem.MaxEditedWeight,
            MaxCount = KsFieldCommanderRuleSystem.MaxEditedCount,
            MaxTime = KsFieldCommanderRuleSystem.MaxEditedTime,
            MaxQueuedPurchases = KsFieldCommanderRuleSystem.MaxQueuedPurchases,
            MaxAnnouncementLength = KsFieldCommanderRuleSystem.MaxQueuedAnnouncementLength,
        };

        if (ResolveShownRule() is not { } rule)
            return state;

        var component = rule.Comp1;
        state.Rule = _entityManager.GetNetEntity(rule.Owner);
        state.Status = BuildStatus(rule);
        state.MaxAliveLimit = component.HardMaxAlive;

        foreach (var phase in component.Phases)
        {
            state.Phases.Add(new KsFieldCommanderAdminPhaseData
            {
                Name = phase.Name,
                Start = phase.Start,
                WaveInterval = phase.WaveInterval,
                Budget = phase.Budget,
            });
        }

        var living = _ruleSystem.CountLivingUnitsByAsset(rule.Owner);
        for (var i = 0; i < component.Assets.Count; i++)
        {
            var asset = component.Assets[i];
            state.Assets.Add(new KsFieldCommanderAdminAssetData
            {
                Entity = asset.Entity?.Id,
                Humanoid = asset.Humanoid?.Id,
                Count = asset.Count,
                Cost = asset.Cost,
                Weight = asset.Weight,
                MinPhase = asset.MinPhase,
                MaxAlive = asset.MaxAlive,
                Delivery = (byte)asset.Delivery,
                Tags = [..asset.Tags],
                GuaranteedEveryWaves = asset.GuaranteedEveryWaves,
                GuaranteedOnPhaseEnter = asset.GuaranteedOnPhaseEnter,
                Disabled = asset.Disabled,
                Living = living.GetValueOrDefault(i),
            });
        }

        return state;
    }

    /// <summary>
    ///     Sends the status strip its refresh.
    /// </summary>
    public void SendStatus()
    {
        var rule = ResolveShownRule();
        SendMessage(new KsFieldCommanderAdminStatusMessage(
            rule is { } shown ? _entityManager.GetNetEntity(shown.Owner) : null,
            rule is { } shownRule ? BuildStatus(shownRule) : new KsFieldCommanderAdminStatusData()));
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is not KsFieldCommanderAdminEuiMessage adminMessage)
            return;

        if (!IsAllowed())
        {
            Close();
            return;
        }

        // Answered with whichever commander is shown now, even when the one it names has ended.
        if (adminMessage is KsFieldCommanderAdminRefreshMessage)
        {
            StateDirty();
            return;
        }

        // Re-resolved for every message: the commander may have ended since the window last heard of it.
        if (!_entityManager.TryGetEntity(adminMessage.Rule, out var ruleUid) ||
            !_ruleSystem.TryGetActiveRule(ruleUid.Value, out var found))
        {
            SendMessage(new KsFieldCommanderAdminResultMessage(false));
            StateDirty();
            return;
        }

        var rule = found.Value;
        _ruleUid = rule.Owner;
        var accepted = Apply(rule, adminMessage);

        SendMessage(new KsFieldCommanderAdminResultMessage(accepted));
        StateDirty();
    }

    private bool Apply(Entity<KsFieldCommanderRuleComponent, GameRuleComponent> rule, KsFieldCommanderAdminEuiMessage message)
    {
        var commander = new Entity<KsFieldCommanderRuleComponent>(rule.Owner, rule.Comp1);

        switch (message)
        {
            case KsFieldCommanderAdminSetPausedMessage setPaused:
                _ruleSystem.SetPaused(commander, setPaused.Paused);
                return true;

            case KsFieldCommanderAdminForceWaveMessage:
                _ruleSystem.ForceWave(commander);
                return true;

            case KsFieldCommanderAdminSetPhaseMessage setPhase:
                if (setPhase.Phase < 0 || setPhase.Phase >= rule.Comp1.Phases.Count)
                    return false;

                _ruleSystem.SetPhase(rule, setPhase.Phase);
                return true;

            case KsFieldCommanderAdminEditPhaseMessage editPhase:
                return _ruleSystem.TryEditPhase(commander, editPhase.Phase, editPhase.Start, editPhase.WaveInterval, editPhase.Budget);

            case KsFieldCommanderAdminEditAssetMessage editAsset:
                return _ruleSystem.TryEditAsset(commander, editAsset.Asset, ToEdit(editAsset.Values));

            case KsFieldCommanderAdminAddAssetMessage addAsset:
                return _ruleSystem.TryAddAsset(commander, ToEdit(addAsset.Values)) >= 0;

            case KsFieldCommanderAdminSetAssetDisabledMessage setDisabled:
                return _ruleSystem.TrySetAssetDisabled(commander, setDisabled.Asset, setDisabled.Disabled);

            case KsFieldCommanderAdminQueueWaveMessage queueWave:
                if (queueWave.Purchases is not { } purchases)
                    return false;

                var list = new List<(int Asset, int Count)>(purchases.Count);
                foreach (var purchase in purchases)
                    list.Add((purchase.Asset, purchase.Count));

                return _ruleSystem.TryQueueWave(commander, list, queueWave.Announcement, queueWave.SendNow);

            default:
                return false;
        }
    }

    private static KsFieldCommanderAssetEdit ToEdit(KsFieldCommanderAdminAssetValues values)
    {
        return new KsFieldCommanderAssetEdit(
            Entity: string.IsNullOrWhiteSpace(values.Entity) ? (EntProtoId?)null : new EntProtoId(values.Entity.Trim()),
            Humanoid: string.IsNullOrWhiteSpace(values.Humanoid) ? (ProtoId<RandomHumanoidSettingsPrototype>?)null : new ProtoId<RandomHumanoidSettingsPrototype>(values.Humanoid.Trim()),
            Count: values.Count,
            Cost: values.Cost,
            Weight: values.Weight,
            MinPhase: values.MinPhase,
            MaxAlive: values.MaxAlive,
            Delivery: values.Delivery is { } delivery ? (KsFieldCommanderDelivery)delivery : null);
    }

    /// <summary>
    ///     The commander shown: the one shown so far while it still runs, else the first running.
    /// </summary>
    private Entity<KsFieldCommanderRuleComponent, GameRuleComponent>? ResolveShownRule()
    {
        if (_ruleUid is { } ruleUid && _ruleSystem.TryGetActiveRule(ruleUid, out var shown))
            return shown;

        var rules = _ruleSystem.GetActiveRules();
        _ruleUid = rules.Count > 0 ? rules[0].Owner : null;
        return rules.Count > 0 ? rules[0] : null;
    }

    private KsFieldCommanderAdminStatusData BuildStatus(Entity<KsFieldCommanderRuleComponent, GameRuleComponent> rule)
    {
        var status = _ruleSystem.GetAdminStatus(rule);
        var data = new KsFieldCommanderAdminStatusData
        {
            PhaseIndex = status.PhaseIndex,
            PhaseName = status.PhaseName,
            Elapsed = status.Elapsed,
            NextPhaseIn = status.NextPhaseIn,
            NextWaveIn = status.NextWaveIn,
            Alive = status.Alive,
            Cap = status.Cap,
            Defenders = status.Defenders,
            CarriedBudget = status.CarriedBudget,
            WavesSent = status.WavesSent,
            Paused = status.Paused,
            DoctrineName = status.DoctrineName,
        };

        foreach (var wave in rule.Comp1.QueuedWaves)
        {
            var waveData = new KsFieldCommanderAdminQueuedWaveData { Announcement = wave.Announcement };
            foreach (var purchase in wave.Purchases)
                waveData.Purchases.Add(new KsFieldCommanderAdminPurchase(purchase.Asset, purchase.Count));

            data.QueuedWaves.Add(waveData);
        }

        return data;
    }
}
