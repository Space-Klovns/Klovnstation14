using Content.Shared.DeviceLinking;
using Jint.Native;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet.Components;

/// <summary>
///
/// </summary>
[RegisterComponent]
public sealed partial class ExecutorComponent : Component
{
    [DataField]
    public List<string> Modules = new();

    [DataField]
    public string Log = "";

    [DataField]
    public string Command = "";

    [DataField]
    public int MemoryAllocation = 1024 * 1024; // 1MB

    [DataField]
    public int MaximumExecutionStatements = 250000;

    [DataField]
    public string SignalPortNaming = "ExecutorPort";

    [DataField]
    public int PortCount = 5;

    [DataField]
    public Dictionary<ProtoId<SinkPortPrototype>, JsValue> ListeningPorts = [];

    [DataField]
    public bool DebugState = false;
}
