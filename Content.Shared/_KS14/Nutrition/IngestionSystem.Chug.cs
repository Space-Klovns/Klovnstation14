using System.Diagnostics.CodeAnalysis;
using Content.Shared.Nutrition.Components;
using Content.Shared.Verbs;

namespace Content.Shared.Nutrition.EntitySystems;

public sealed partial class IngestionSystem
{
    public bool TryGetChugVerb(EntityUid user, Entity<EdibleComponent> ingested, [NotNullWhen(true)] out AlternativeVerb? verb)
    {
        verb = null;

        if (!CanIngest(user, ingested.Owner))
            return false;

        verb = new()
        {
            Act = () =>
            {
                TryChug(user, ingested.Owner);
            },
            Icon = _proto.Index(Drink).VerbIcon,
            Text = Loc.GetString("ingestion-verb-chug"),
            Priority = 10 // Higher priority than standard drink (Priority = 2) so that it is the default for alt-activating
        };

        return true;
    }

    public bool TryChug(EntityUid user, EntityUid ingested)
    {
        return AttemptIngest(user, user, ingested, true, true);
    }
}
