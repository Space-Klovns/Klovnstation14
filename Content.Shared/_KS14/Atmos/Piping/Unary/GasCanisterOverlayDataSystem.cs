using System.Numerics;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Atmos.Piping.Unary.Systems;
using Robust.Shared.Network;

namespace Content.Shared._KS14.Atmos.Piping.Unary;

public sealed partial class GasCanisterOverlayDataSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private SharedAtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private SharedGasTileOverlaySystem _gasTileOverlaySystem = default!;
    [Dependency] private KsFluidmosPropertiesSystem _fluidmosPropertiesSystem = default!;

    [Dependency] private EntityQuery<GasCanisterComponent> _canisterQuery = default!;

    [SubscribeLocalEvent]
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

            // A canister of nothing but liquid has no moles to divide by.
            var newValue = totalMoles > 0f ? (byte)(canisterComponent.Air[allGasId] / totalMoles * byte.MaxValue) : (byte)0;
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

        UpdateFluidAppearance((entity.Owner, entity.Comp), canisterComponent.Air.Fluid);
    }

    /// <summary>
    ///     The liquid and ice levels in the canister's window, and the liquid's colour. Dirtied only when they change.
    /// </summary>
    private void UpdateFluidAppearance(Entity<GasCanisterOverlayComponent> entity, KsFluidMixture? fluid)
    {
        var liquidFill = Fill(entity.Comp, fluid?.LiquidUnits ?? 0f);
        var solidFill = Fill(entity.Comp, fluid?.SolidUnits ?? 0f);
        var liquidColor = entity.Comp.LiquidColor;

        if (fluid != null && liquidFill > 0)
        {
            var mixed = Vector4.Zero;
            var weight = 0f;
            foreach (var (key, amount) in fluid.Reagents)
            {
                if (amount.Liquid <= 0f || !_fluidmosPropertiesSystem.TryGetProperties(key.Prototype, out var properties))
                    continue;

                var color = properties.Color;
                mixed += new Vector4(color.R, color.G, color.B, color.A) * amount.Liquid;
                weight += amount.Liquid;
            }

            if (weight > 0f)
            {
                mixed /= weight;
                liquidColor = new Color(mixed.X, mixed.Y, mixed.Z, mixed.W);
            }
        }

        if (liquidFill != entity.Comp.LiquidFill)
        {
            entity.Comp.LiquidFill = liquidFill;
            DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.LiquidFill));
        }

        if (solidFill != entity.Comp.SolidFill)
        {
            entity.Comp.SolidFill = solidFill;
            DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.SolidFill));
        }

        if (liquidColor != entity.Comp.LiquidColor)
        {
            entity.Comp.LiquidColor = liquidColor;
            DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.LiquidColor));
        }
    }

    private static byte Fill(GasCanisterOverlayComponent component, float units)
    {
        if (units <= 0f || component.FullWindowUnits <= 0f)
            return 0;

        // At least one step, so any liquid at all shows.
        return (byte)Math.Clamp(MathF.Ceiling(units / component.FullWindowUnits * byte.MaxValue), 1f, byte.MaxValue);
    }

    [SubscribeLocalEvent(after: [typeof(SharedGasCanisterSystem)])]
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
