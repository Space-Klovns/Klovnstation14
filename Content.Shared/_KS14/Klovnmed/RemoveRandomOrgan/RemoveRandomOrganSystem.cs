using Content.Shared._KS14.Random.Helpers;
using Content.Shared.Body;
using Robust.Shared.Collections;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Klovnmed.RemoveRandomOrgan;

public sealed class RemoveRandomOrganSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RemoveRandomOrganComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<RemoveRandomOrganComponent> entity, ref MapInitEvent args)
    {
        if (!TryComp<BodyComponent>(entity, out var bodyComponent))
            goto end;

        var eligible = new ValueList<EntityUid>();
        foreach (var organUid in bodyComponent.RecursiveChildUids)
        {
            var organComponent = Comp<OrganComponent>(organUid);
            if (organComponent.Category is not { } category ||
                !entity.Comp.Categories.Contains(category))
                continue;

            eligible.Add(organUid);
        }

        if (eligible.Count == 0)
            goto end;

        var predictedRandom = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(KsSharedRandomExtensions.GetNetId(entity.Owner, EntityManager), (int)_gameTiming.CurTick.Value);
        var removedUid = eligible[predictedRandom.Next(eligible.Count)];

        QueueDel(removedUid);

    end:
        RemComp(entity, entity.Comp);
    }
}
