// KS14: added in this fork
namespace Content.Shared.Speech;

public sealed partial class ListenEvent
{
    /// <summary>The text before accents. Absent for muffled deliveries.</summary>
    public string? KsTranslationText;
}
