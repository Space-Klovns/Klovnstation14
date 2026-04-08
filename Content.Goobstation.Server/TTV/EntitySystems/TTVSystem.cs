using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Goobstation.Client.TTV;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.CCVar;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Containers;
using IConfigurationManager = Robust.Shared.Configuration.IConfigurationManager;

namespace Content.Goobstation.Server.TTV;

public sealed class TTVSystem : SharedTTVSystem
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] ItemSlotsSystem _slotsSystem = default!;
    [Dependency] ExplosionSystem _explosionSystem = default!;

    // Timers are the same as the ones from GasTankSystem
    private const float TimerDelay = 0.5f;
    private float _timer = 0f;

    private float _maxExplosionRange;

    /// <summary>How many times will the TTV react to build up before exploding?</summary>
    public const int IgnitionReactTimes = 3;
    public const float FragmentPressure = 84 * Atmospherics.OneAtmosphere;
    public const float FragmentScale = 84 * Atmospherics.OneAtmosphere;

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

        _timer -= TimerDelay;

        var ttvQuery = EntityQueryEnumerator<TTVComponent>();
        while (ttvQuery.MoveNext(out var uid, out var ttvComponent))
        {
            if (!ttvComponent.Open || ttvComponent.Igniting)
                continue;

            if (!UpdateTTV(uid, out var mixture))
                continue;

            if (mixture.Temperature >= Atmospherics.T0C + 400)
                StartExploding((uid, ttvComponent));
        }
    }

    /// <summary>
    /// Reacts and then equalises contents of every tank connected to a TTV.
    /// </summary>
    /// <returns>Whether the TTV was updated.</returns>
    public bool UpdateTTV(EntityUid ttv, [NotNullWhen(true)] out GasMixture? mixture)
    {
        if (!TryComp<ItemSlotsComponent>(ttv, out var slotsComponent))
        {
            mixture = null;
            return false;
        }

        GasMixture mergedMixture = new();
        List<GasMixture> affectedMixtures = new();

        foreach (var (_, slot) in slotsComponent.Slots)
        {
            if (slot.Item is not { } itemUid || !GasTankQuery.TryComp(itemUid, out var itemGasTankComponent))
                continue;

            var airToMerge = itemGasTankComponent.Air;

            _atmosphereSystem.React(airToMerge, itemGasTankComponent);

            mergedMixture.Volume += airToMerge.Volume;
            _atmosphereSystem.Merge(mergedMixture, airToMerge);
            affectedMixtures.Add(airToMerge);
        }

        _atmosphereSystem.DivideInto(mergedMixture, affectedMixtures);

        mixture = mergedMixture;
        return true;
    }

    public void StartExploding(Entity<TTVComponent> ttv)
    {
        if (!TryComp<ItemSlotsComponent>(ttv, out var slotsComponent))
            return;

        GasMixture combinedMixture = new(volume: 0f);
        int mixtureCount = 0;

        foreach (var (_, slot) in slotsComponent.Slots)
        {
            _slotsSystem.SetLock(ttv, slot, true, slotsComponent);
            if (slot.Item is not { } itemUid || !GasTankQuery.TryComp(itemUid, out var itemGasTankComponent))
                continue;

            var airToMerge = itemGasTankComponent.Air;

            _atmosphereSystem.Merge(combinedMixture, airToMerge);
            combinedMixture.Volume += airToMerge.Volume;

            ++mixtureCount;

            QueueDel(itemUid);
        }

        if (mixtureCount == 0)
            return;

        _explosionSystem.TriggerExplosive(ttv, radius: Ignite(combinedMixture));
    }

    /// <summary>Explodes and gets the explosion power of a mixture.</summary>
    public float Ignite(GasMixture mixture)
    {
        for (int i = 0; i < IgnitionReactTimes; ++i)
            _atmosphereSystem.React(mixture, null);

        return mixture.Volume * (mixture.Pressure - FragmentPressure) / FragmentScale;
    }
}
