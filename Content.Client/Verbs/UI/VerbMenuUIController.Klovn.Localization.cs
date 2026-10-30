using Content.Shared.Verbs;

namespace Content.Client.Verbs.UI;

public sealed partial class VerbMenuUIController
{
    private void MergeServerVerbs(List<Verb> verbs)
    {
        foreach (var verb in verbs)
        {
            if (CurrentVerbs.TryGetValue(verb, out var local))
            {
                if (local.ClientExclusive)
                    continue;
                CurrentVerbs.Remove(local);
            }
            CurrentVerbs.Add(verb);
        }
    }
}
