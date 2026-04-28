using Content.Shared.Atmos;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Atmos.Piping.Unary.Systems;
using Robust.Shared.Network;

namespace Content.Shared._KS14.Atmos.Piping.Unary;

public sealed class GasCanisterOverlayDataSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly SharedAtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private readonly SharedGasTileOverlaySystem _gasTileOverlaySystem = default!;

    private EntityQuery<GasCanisterComponent> _canisterQuery;

    public override void Initialize()
    {
        base.Initialize();

        _canisterQuery = GetEntityQuery<GasCanisterComponent>();

        SubscribeLocalEvent<GasCanisterOverlayComponent, ComponentInit>(OnCanisterInit);
        SubscribeLocalEvent<GasCanisterOverlayComponent, MapInitEvent>(OnCanisterMapInit, after: [typeof(SharedGasCanisterSystem)]);
    }

    private void OnCanisterInit(Entity<GasCanisterOverlayComponent> entity, ref ComponentInit args)
    {
        entity.Comp.AppearanceGasPercentages = new byte[_gasTileOverlaySystem.VisibleGasId.Length];
    }

    public void UpdateCanisterAppearance(Entity<GasCanisterOverlayComponent?> entity, GasCanisterComponent canisterComponent, bool regardSimilarPercentages = true)
    {
        if (!Resolve(entity, ref entity.Comp))
            return;

        var totalMoles = canisterComponent.Air.TotalMoles;
        entity.Comp.NetworkedMoles = totalMoles;
        DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.NetworkedMoles));

        var similars = 0;
        for (var i = 0; i < _gasTileOverlaySystem.VisibleGasId.Length; i++)
        {
            var allGasId = _gasTileOverlaySystem.VisibleGasId[i];

            var newValue = (byte)(canisterComponent.Air[allGasId] / totalMoles * byte.MaxValue);
            if (entity.Comp.AppearanceGasPercentages[i] == newValue)
                similars++;

            entity.Comp.AppearanceGasPercentages[i] = newValue;
        }
        // don't dirty if entire array was the same
        if (regardSimilarPercentages &&
            similars != _gasTileOverlaySystem.VisibleGasId.Length)
            DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.AppearanceGasPercentages));

        // Do firestate
        if (!_atmosphereSystem.IsMixtureIgnitable(canisterComponent.Air) ||
            canisterComponent.Air.Temperature < Atmospherics.FireMinimumTemperatureToExist)
        {
            entity.Comp.FireState = 0;
        }
        else // random number lol
            entity.Comp.FireState = canisterComponent.Air.Temperature > 750f ? (byte)3 : (byte)2;

        DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.FireState));
    }

    private void OnCanisterMapInit(Entity<GasCanisterOverlayComponent> entity, ref MapInitEvent args)
    {
        if (!_netManager.IsServer)
            return;

        if (!_canisterQuery.TryGetComponent(entity.Owner, out var canisterComponent))
            return;


        // dgaf about similar percentages, as on mapinit every value will be the same as previous(?): 0
        UpdateCanisterAppearance(entity!, canisterComponent, regardSimilarPercentages: false);
    }
}
