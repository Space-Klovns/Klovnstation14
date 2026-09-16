namespace Content.Client._KS14.Actions;

/// <summary>
/// Matching rules for persisted action identities.
/// </summary>
internal static class KsActionBarIdentity
{
    public static bool MatchesSaved(KsSavedActionIdentity saved, KsSavedActionIdentity current)
    {
        return saved.ActionPrototype == current.ActionPrototype &&
               saved.Occurrence == current.Occurrence &&
               NormalizeProvider(saved.ProviderPrototype) == NormalizeProvider(current.ProviderPrototype);
    }

    public static string? NormalizeProvider(string? providerPrototype)
    {
        return string.IsNullOrWhiteSpace(providerPrototype) ? null : providerPrototype;
    }
}
