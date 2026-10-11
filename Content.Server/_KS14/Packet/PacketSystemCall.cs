using System.Threading;

namespace Content.Server._KS14.Packet;

public sealed class PacketSystemCall(int thread, ManualResetEventSlim? pauseHandler, Action action)
{
    public int ThreadID = thread;
    public ManualResetEventSlim? PauseHandler = pauseHandler;
    public Action Action = action;
}
