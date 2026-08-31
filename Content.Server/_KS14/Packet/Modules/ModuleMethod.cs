using System.Threading.Channels;
using Content.Server._KS14.Packet.Components;

namespace Content.Server._KS14.Packet;

public abstract class ModuleMethod
{
    public abstract PacketModule? Module { get; set; }

    public string Id = "method";

    public abstract object ModuleExec { get; }

    public Channel<object> Channel { get; protected set; } = System.Threading.Channels.Channel.CreateUnbounded<object>();

    public ModuleMethod(PacketModule? module) { }
}
