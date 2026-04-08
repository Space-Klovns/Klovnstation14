using Content.Goobstation.Shared.Ordnance.TTV;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.CCVar;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Containers;
using IConfigurationManager = Robust.Shared.Configuration.IConfigurationManager;

namespace Content.Goobstation.Server.Ordnance.TTV;

public sealed class TTVSystem : SharedTTVSystem
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private readonly ExplosionSystem _explosionSystem = default!;

    // Timers are the same as the ones from GasTankSystem
    private const float TimerDelay = 0.5f;
    private float _timer = 0f;

    private float _maxExplosionRange;

    /// <summary>How many times will the TTV react to build up before exploding?</summary>
    public const int IgnitionReactTimes = 3;
    public const float FragmentPressure = 40f * Atmospherics.OneAtmosphere;
    // On /tg/, this was 84atm, calibrated so that a TTV assembled using two 70L normal air tanks will maxcap at atleast 160atm. However, normal airtanks on SS14 are 5L so I made this 6.65atm.
    public const float FragmentScale = 6.65f * Atmospherics.OneAtmosphere;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_configurationManager, CCVars.AtmosTankFragment, x => _maxExplosionRange = x, true);

        SubscribeLocalEvent<InsideTTVComponent, TTVTankUpdateAttemptEvent>(OnTankUpdateAttempt);

        SubscribeLocalEvent<TTVComponent, EntInsertedIntoContainerMessage>(OnTankInserted);
        SubscribeLocalEvent<InsideTTVComponent, EntGotRemovedFromContainerMessage>(OnTankRemoved);
    }

    private void OnTankInserted(Entity<TTVComponent> ttv, ref EntInsertedIntoContainerMessage args)
        => EnsureComp<InsideTTVComponent>(args.Entity);

    private void OnTankRemoved(Entity<InsideTTVComponent> ttv, ref EntGotRemovedFromContainerMessage args)
        => RemComp(ttv, ttv.Comp);

    private void OnTankUpdateAttempt(Entity<InsideTTVComponent> _, ref TTVTankUpdateAttemptEvent args)
        => args.Cancelled = true;


    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _timer += frameTime;
        if (_timer < TimerDelay)
            return;

        _timer = 0;

        var ttvQuery = EntityQueryEnumerator<TTVComponent, ItemSlotsComponent>();
        while (ttvQuery.MoveNext(out var uid, out var ttvComponent, out var slotsComponent))
        {
            if (!ttvComponent.Open)
                continue;

            EqualizeTTV((uid, slotsComponent), out var mixture);

            if (!ttvComponent.Igniting &&
                (mixture.Temperature >= Atmospherics.T0C + 400 ||
                mixture.Pressure > FragmentPressure))
                StartExploding((uid, ttvComponent, slotsComponent));
        }
    }

    /// <summary>
    ///     Reacts and then equalises contents of every tank connected to a TTV.
    ///         This can lose gas due to inaccuracy.
    /// </summary>
    /// <returns>Whether the TTV was updated.</returns>
    public void EqualizeTTV(Entity<ItemSlotsComponent> ttv, out GasMixture mixture)
    {
        GasMixture mergedMixture = new();
        List<GasMixture> affectedMixtures = new();

        foreach (var (_, slot) in ttv.Comp.Slots)
        {
            if (slot.Item is not { } itemUid || !GasTankQuery.TryComp(itemUid, out var itemGasTankComponent))
                continue;

            var airToMerge = itemGasTankComponent.Air;

            _atmosphereSystem.React(airToMerge, itemGasTankComponent);

            mergedMixture.Volume += airToMerge.Volume;
            _atmosphereSystem.Merge(mergedMixture, airToMerge);

            airToMerge.Clear();
            affectedMixtures.Add(airToMerge);
        }

        _atmosphereSystem.DivideInto(mergedMixture, affectedMixtures);
        mixture = mergedMixture;
    }

    public void StartExploding(Entity<TTVComponent, ItemSlotsComponent> ttv)
    {
        var slotsComponent = ttv.Comp2;
        ttv.Comp1.Igniting = true;

        GasMixture combinedMixture = new(volume: 0f);
        int mixtureCount = 0;

        foreach (var (_, slot) in slotsComponent.Slots)
        {
            if (slot.Item is not { } itemUid || !GasTankQuery.TryComp(itemUid, out var itemGasTankComponent))
                continue;

            var airToMerge = itemGasTankComponent.Air;

            _atmosphereSystem.Merge(combinedMixture, airToMerge);
            combinedMixture.Volume += airToMerge.Volume;

            ++mixtureCount;
        }

        if (mixtureCount == 0)
            return;

        ttv.Comp1.Igniting = false;
        _explosionSystem.TriggerExplosive(ttv, delete: false, radius: Ignite(combinedMixture));
    }

    /// <summary>Explodes and gets the explosion power of a mixture.</summary>
    public float Ignite(GasMixture mixture)
    {
        for (int i = 0; i < IgnitionReactTimes; ++i)
            _atmosphereSystem.React(mixture, null);

        return mixture.Volume * (mixture.Pressure - FragmentPressure) / FragmentScale;
    }
}
