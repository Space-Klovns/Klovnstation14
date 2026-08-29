using Content.Server._KS14.Packet.Components;
using Content.Server.Chat.Systems;
using Content.Server.DeviceLinking.Systems;
using Content.Server.GameTicking.Events;
using Content.Server.Popups;
using Content.Shared._KS14.Packets.BUI;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Jint;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._KS14.Packet;

/// <summary>
/// Handles events and network.
/// </summary>
public sealed partial class PacketSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private UserInterfaceSystem _userInterfaceSystem = default!;
    [Dependency] private PopupSystem _popupSystem = default!;
    [Dependency] private DeviceLinkSystem _deviceLinkSystem = default!;

    internal int _mainThreadId;

    public override void Initialize()
    {
        _mainThreadId = Environment.CurrentManagedThreadId;
        PreInitJint();

        SubscribeLocalEvent<ExecutorComponent, BoundUIOpenedEvent>(SubscribeUpdateUiState);
        SubscribeLocalEvent<ExecutorComponent, SaveExecutorCommandMessage>(OnSave);
        SubscribeLocalEvent<ExecutorComponent, StartExecutionMessage>(OnExecute);
        SubscribeLocalEvent<ExecutorComponent, SignalReceivedEvent>(OnExecutorSignal);

        SubscribeLocalEvent<PacketNetworkComponent, ComponentInit>(OnPacketInit);

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStart);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);

    }

    public override void Update(float frameTime)
    {
        UpdateSystemCalls();
        foreach (var (uid, _) in _executorEntities)
        {
            if (uid.Comp.Log == string.Empty)
                continue;

            _userInterfaceSystem.ServerSendUiMessage(uid.Owner, ExecutorUiKey.Key, new LogExecutorMessage(uid.Comp.Log));
            uid.Comp.Log = string.Empty;
        }
    }

    private void SubscribeUpdateUiState<T>(Entity<ExecutorComponent> ent, ref T ev)
    {
        UpdateUiState(ent);
    }

    private void OnSave(Entity<ExecutorComponent> ent, ref SaveExecutorCommandMessage ev)
    {
        ent.Comp.Command = ev.Command;
    }

    private void OnExecute(Entity<ExecutorComponent> ent, ref StartExecutionMessage ev)
    {
        ExecuteCommand(ent.Comp.Command, ent);
    }

    private void OnExecutorSignal(Entity<ExecutorComponent> ent, ref SignalReceivedEvent ev)
    {
        if (!ent.Comp.ListeningPorts.ContainsKey(ev.Port))
            return;

        OnSignal(ev.Port, ent);
    }

    private void UpdateUiState(Entity<ExecutorComponent> ent)
    {
        var maxStatements = ent.Comp.MaximumExecutionStatements;
        var maxMemory = ent.Comp.MemoryAllocation;
        var command = ent.Comp.Command;

        _userInterfaceSystem.SetUiState(ent.Owner, ExecutorUiKey.Key, new ExecutorBoundUserInterfaceState(maxStatements, maxMemory, command));
    }

    private void OnRoundStart(RoundStartingEvent ev)
    {
        RandomizeFrequencies();
    }

    private void OnPacketInit(Entity<PacketNetworkComponent> ent, ref ComponentInit ev)
    {
        SetupAddress(ent);
        ReloadFrequencies(ent);
    }

    private void OnCleanup(RoundRestartCleanupEvent ev)
    {
        Dispose();
    }

    private void Dispose()
    {
        _packetEntities.Clear();
        _executorEntities.Clear();
        _methods.Clear();
        _modules.Clear();
        _engineCts.Clear();
        _frequencies.Clear();
        _networks.Clear();
    }
}
