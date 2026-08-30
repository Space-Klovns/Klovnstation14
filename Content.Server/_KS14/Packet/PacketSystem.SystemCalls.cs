using System.Threading.Channels;
using System.Threading.Tasks;

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

    public async Task<T> TryWrapSystemCall<T>(Func<T> func, Channel<object> channel)
    {
        if (Environment.CurrentManagedThreadId == _mainThreadId)
            return func.Invoke();

        WrapSystemCall(async void () =>
        {
            await channel.Writer.WriteAsync(func.Invoke()!);
        });

        return (T) await channel.Reader.ReadAsync();
    }

    public void TryWrapSystemCall(Action action)
    {
        if (Environment.CurrentManagedThreadId == _mainThreadId)
            action.Invoke();

        WrapSystemCall(action);
    }

    public void WrapSystemCall(Action action)
    {
        _callQueue.Enqueue(action);
    }
}
