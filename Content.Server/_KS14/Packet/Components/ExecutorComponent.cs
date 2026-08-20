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
}
