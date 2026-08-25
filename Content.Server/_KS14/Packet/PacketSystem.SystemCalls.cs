namespace Content.Server._KS14.Packet;

/// <summary>
/// This handles...
/// </summary>
public sealed partial class PacketSystem
{
    private Queue<Action> _callQueue = new();

    private void UpdateSystemCalls()
    {
        while (_callQueue.Count > 0)
        {
            var action = _callQueue.Dequeue();
            action.Invoke();
        }
    }

    public void WrapSystemCall(Action action)
    {
        _callQueue.Enqueue(action);
    }
}
