using Content.Shared._KS14.Language; // KS14

namespace Content.Shared.Speech;

public sealed class ListenEvent : EntityEventArgs
{
    public readonly string Message;
    public readonly EntityUid Source;
    public readonly KsUtteranceContext? KsLanguage; // KS14

    public ListenEvent(string message, EntityUid source, KsUtteranceContext? ksLanguage = null /* KS14 */)
    {
        Message = message;
        Source = source;
        KsLanguage = ksLanguage; // KS14
    }
}

public sealed class ListenAttemptEvent : CancellableEntityEventArgs
{
    public readonly EntityUid Source;

    public ListenAttemptEvent(EntityUid source)
    {
        Source = source;
    }
}
