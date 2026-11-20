using Content.Shared.Body.Events;
using Content.Shared.Metabolism;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Perks.Healing;

/// <summary>
///     Metabolises <see cref="RateMultiplier"/> times as fast: medicine works sooner, and wears off sooner.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsMetabolismRateSystem))]
public sealed partial class KsMetabolismRateAbilityComponent : Component
{
    /// <summary>
    ///     How many times as fast; 1 changes nothing.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float RateMultiplier = 1.5f;
}

public sealed partial class KsMetabolismRateSystem : EntitySystem
{
    [Dependency] private MetabolizerSystem _metabolizerSystem = default!;

    [SubscribeLocalEvent]
    private void OnGetMultiplier(Entity<KsMetabolismRateAbilityComponent> entity, ref GetMetabolicMultiplierEvent args)
    {
        // The multiplier stretches the update interval, so a faster rate divides it.
        if (entity.Comp.LifeStage <= ComponentLifeStage.Running && entity.Comp.RateMultiplier > 0f)
            args.Multiplier /= entity.Comp.RateMultiplier;
    }

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsMetabolismRateAbilityComponent> entity, ref ComponentStartup args)
    {
        _metabolizerSystem.UpdateMetabolicMultiplier(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsMetabolismRateAbilityComponent> entity, ref ComponentShutdown args)
    {
        // Shut down already, so OnGetMultiplier leaves it out.
        if (!TerminatingOrDeleted(entity.Owner))
            _metabolizerSystem.UpdateMetabolicMultiplier(entity.Owner);
    }
}
