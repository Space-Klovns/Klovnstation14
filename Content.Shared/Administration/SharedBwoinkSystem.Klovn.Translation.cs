namespace Content.Shared.Administration;

public abstract partial class SharedBwoinkSystem
{
    public sealed partial class BwoinkTextMessage
    {
        // Set by the server only. Incoming client values are ignored when it reconstructs the message.
        public Guid? KsTranslationId { get; init; }
        public string? KsTranslationText { get; init; }
    }
}
