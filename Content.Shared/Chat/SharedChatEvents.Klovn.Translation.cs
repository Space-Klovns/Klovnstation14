// KS14: added in this fork
namespace Content.Shared.Chat;

public sealed partial class EntitySpokeEvent
{
    /// <summary>The plain text before accents, for translation of clear deliveries.</summary>
    public string? KsTranslationText;
}
