using System.Threading.Channels;
using Content.Server._KS14.Packet.Components;

namespace Content.Server._KS14.Packet;

public abstract class ModuleMethod
{
    public abstract Module? Module { get; set; }

    public string Id = "method";

    public abstract object ModuleExec { get; }

    public Channel<Object> _channel = Channel.CreateUnbounded<object>();

    public ModuleMethod(Module? module) { }
}
