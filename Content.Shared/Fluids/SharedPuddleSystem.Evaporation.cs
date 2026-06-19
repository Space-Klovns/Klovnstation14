using System.Linq;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Content.Shared.Fluids.Components;
using Content.Shared.Chemistry.Reagent;

namespace Content.Shared.Fluids;

public abstract partial class SharedPuddleSystem
{
    private static readonly TimeSpan EvaporationCooldown = TimeSpan.FromSeconds(1);

    private void OnEvaporationMapInit(Entity<EvaporationComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextTick = _timing.CurTime + EvaporationCooldown;
        Dirty(ent);
    }

    private void UpdateEvaporation(Entity<PuddleComponent> entity, Solution solution)
    {
        // KS14 - Start
        // Calculate evaporation speed, including dynamic modifications (e.g. Evaporin gas).
        var speeds = GetEvaporationSpeeds(solution);
        var baseSpeed = speeds.Count > 0 ? speeds.Values.Sum() / speeds.Count : FixedPoint2.Zero;
        var modifiedSpeed = baseSpeed;

        ModifyEvaporationRate(entity, ref modifiedSpeed);

        if (modifiedSpeed > FixedPoint2.Zero)
        {
            if (!_evaporationQuery.HasComp(entity))
            {
                var evaporation = AddComp<EvaporationComponent>(entity);
                evaporation.NextTick = _timing.CurTime + EvaporationCooldown;
                Dirty(entity.Owner, evaporation);
            }
        }
        else
        {
            if (_evaporationQuery.HasComp(entity))
                RemComp<EvaporationComponent>(entity);
        }
        // KS14 - End
    }

    private void TickEvaporation()
    {
        var query = EntityQueryEnumerator<EvaporationComponent, PuddleComponent>();
        var curTime = _timing.CurTime;
        while (query.MoveNext(out var uid, out var evaporation, out var puddle))
        {
            if (evaporation.NextTick > curTime)
                continue;

            // Necessary to keep client and server in sync so they don't drift
            evaporation.NextTick += EvaporationCooldown;
            Dirty(uid, evaporation);

            if (!_solutionContainerSystem.ResolveSolution(uid, puddle.SolutionName, ref puddle.Solution, out var puddleSolution))
                continue;

            // If we have multiple evaporating reagents in one puddle, just take the average evaporation speed and apply
            // that to all of them.
            var evaporationSpeeds = GetEvaporationSpeeds(puddleSolution);

            // KS14 - Start
            var baseEvaporationSpeed = evaporationSpeeds.Count > 0 ? evaporationSpeeds.Values.Sum() / evaporationSpeeds.Count : FixedPoint2.Zero;
            var modifiedSpeed = baseEvaporationSpeed;

            ModifyEvaporationRate((uid, puddle), ref modifiedSpeed);

            if (modifiedSpeed <= FixedPoint2.Zero)
                continue;

            var evaporinForced = modifiedSpeed > baseEvaporationSpeed;
            Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> reagentProportions;

            if (evaporinForced)
            {
                // Evaporin is present: force evaporation of the ENTIRE puddle, regardless of contents.
                reagentProportions = puddleSolution.Contents.ToDictionary(
                    r => new ProtoId<ReagentPrototype>(r.Reagent.Prototype), 
                    r => r.Quantity / puddleSolution.Volume);
            }
            else
            {
                // Vanilla behavior: only evaporate naturally evaporating reagents.
                reagentProportions = evaporationSpeeds.ToDictionary(
                    kv => kv.Key, 
                    kv => puddleSolution.GetTotalPrototypeQuantity(kv.Key) / puddleSolution.Volume);
            }
            // KS14 - End

            // Still have to iterate over one-by-one since the full solution could have non-evaporating solutions.
            foreach (var (reagent, factor) in reagentProportions)
            {
                var reagentTick = evaporation.EvaporationAmount * EvaporationCooldown.TotalSeconds * modifiedSpeed * factor;
                puddleSolution.SplitSolutionWithOnly(reagentTick, reagent);
            }

            // Despawn if we're done
            if (puddleSolution.Volume == FixedPoint2.Zero)
            {
                // Spawn a *sparkle*
                if (_net.IsServer) // TODO: Change this once we have entity spawn prediction V2
                    SpawnAttachedTo(evaporation.EvaporationEffect, Transform(uid).Coordinates);
                PredictedQueueDel(uid);
            }

            _solutionContainerSystem.UpdateChemicals(puddle.Solution.Value);
        }
    }


    public ProtoId<ReagentPrototype>[] GetEvaporatingReagents(Solution solution)
    {
        List<ProtoId<ReagentPrototype>> evaporatingReagents = [];
        foreach (var solProto in solution.GetReagentPrototypes(_prototypeManager).Keys)
        {
            if (solProto.EvaporationSpeed > FixedPoint2.Zero)
                evaporatingReagents.Add(solProto.ID);
        }
        return evaporatingReagents.ToArray();
    }

    public ProtoId<ReagentPrototype>[] GetAbsorbentReagents(Solution solution)
    {
        var absorbentReagents = new List<ProtoId<ReagentPrototype>>();
        foreach (ReagentPrototype solProto in solution.GetReagentPrototypes(_prototypeManager).Keys)
        {
            if (solProto.Absorbent)
                absorbentReagents.Add(solProto.ID);
        }
        return absorbentReagents.ToArray();
    }

    public bool CanFullyEvaporate(Solution solution)
    {
        return solution.GetTotalPrototypeQuantity(GetEvaporatingReagents(solution)) == solution.Volume;
    }

    /// <summary>
    /// Gets a mapping of evaporating speed of the reagents within a solution.
    /// The speed at which a solution evaporates is the average of the speed of all evaporating reagents in it.
    /// </summary>
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> GetEvaporationSpeeds(Solution solution)
    {
        Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> evaporatingSpeeds = [];
        foreach (var solProto in solution.GetReagentPrototypes(_prototypeManager).Keys)
        {
            if (solProto.EvaporationSpeed > FixedPoint2.Zero)
            {
                evaporatingSpeeds.Add(solProto.ID, solProto.EvaporationSpeed);
            }
        }
        return evaporatingSpeeds;
    }
}
