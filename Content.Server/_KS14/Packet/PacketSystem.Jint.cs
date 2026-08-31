using System.Threading;
using Content.Server._KS14.Packet.Components;
using Jint;

namespace Content.Server._KS14.Packet;

/// <summary>
/// This handles...
/// </summary>
public sealed partial class PacketSystem
{
    /// <summary>
    /// Active executor entities. Normally spawned entities wont have engines.
    /// Entity will receive and cache jint engine only after first execution.
    /// Packets use their own engine.
    /// </summary>
    private Dictionary<Entity<ExecutorComponent>, Engine> _executorEntities = new();

    private Dictionary<Engine, CancellationTokenSource> _engineCts = new();

    /// <summary>
    /// Engine that is used by all packets. Extremely weak compared to executors, having 25 statements limit.
    /// </summary>
    private Engine _packetEngine = new(options =>
    {
        options.MaxStatements(25);
    });

    public void ExecuteCommand(string command, Entity<ExecutorComponent> executor)
    {
        executor.Comp.ListeningPorts.Clear(); // Dispose ports to init them again.
        if (TryComp<PacketNetworkComponent>(executor, out var receiver))
            ReloadFrequencies((executor, receiver));

        var engine = EnsureEngine(executor);
        EnsureToken(engine);

        try
        {
            engine.ExecuteAsync(command);
        }
        catch (Exception e)
        {
            executor.Comp.Log += e.Message + '\n';
        }
    }

    public Engine EnsureEngine(Entity<ExecutorComponent> executor)
    {
        if (_executorEntities.TryGetValue(executor, out var exEngine))
            return exEngine;

        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var engine = new Engine(options =>
        {
            options.MaxStatements(executor.Comp.MaximumExecutionStatements);
            options.LimitMemory(executor.Comp.MemoryAllocation);
            options.ExperimentalFeatures = ExperimentalFeature.TaskInterop;
            options.Constraints.PromiseTimeout = TimeSpan.FromMinutes(3);
        });

        _engineCts.Add(engine, cts);
        _executorEntities.Add(executor, engine);
        InitializeModules(executor);
        SetConstants(executor);
        LoadMethods(executor);
        InitializePorts(executor);

        return engine;
    }

    public CancellationTokenSource EnsureToken(Engine engine)
    {
        if (_engineCts.TryGetValue(engine, out var cts))
            return cts;

        cts = new CancellationTokenSource();
        _engineCts.Add(engine, cts);

        return cts;
    }

    private void SetConstants(Entity<ExecutorComponent> executor)
    {
        var engine = EnsureEngine(executor);

        if (TryComp<PacketNetworkComponent>(executor, out var receiver))
        {
            engine.SetValue("SELF_FREQ", GetFrequency(receiver.Frequency));
            engine.SetValue("SELF_ADD", receiver.Address);
        }
    }
}
