namespace Content.Client._KS14.Actions;

/// <summary>
///     Matching rules for persisted action identities.
/// </summary>
public static class KsActionBarIdentity
{
    /// <summary>
    ///     Whether a saved identity refers to the same action as a live one.
    /// </summary>
    public static bool MatchesSaved(KsSavedActionIdentity saved, KsSavedActionIdentity current)
    {
        return saved.ActionPrototype == current.ActionPrototype &&
               saved.Occurrence == current.Occurrence &&
               NormalizeProvider(saved.ProviderPrototype) == NormalizeProvider(current.ProviderPrototype);
    }

    /// <summary>
    ///     Collapses an absent provider to null, so that a missing, empty and whitespace provider all
    ///         compare equal.
    /// </summary>
    public static string? NormalizeProvider(string? providerPrototype)
    {
        return string.IsNullOrWhiteSpace(providerPrototype) ? null : providerPrototype;
    }
}
