using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using JetBrains.Annotations;

namespace Content.Server.Atmos.Piping.Unary.EntitySystems
{
    [UsedImplicitly]
    public sealed partial class GasPassiveVentSystem : EntitySystem
    {
        [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
        [Dependency] private Content.Server._KS14.Fluidmos.KsFluidmosTileSystem _ksFluidmosTileSystem = default!; // KS14
        [Dependency] private NodeContainerSystem _nodeContainer = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<GasPassiveVentComponent, AtmosDeviceUpdateEvent>(OnPassiveVentUpdated);
        }

        private void OnPassiveVentUpdated(EntityUid uid, GasPassiveVentComponent vent, ref AtmosDeviceUpdateEvent args)
        {
            var environment = _atmosphereSystem.GetContainingMixture(uid, args.Grid, args.Map, true, true);

            if (environment == null)
                return;

            if (!_nodeContainer.TryGetNode(uid, vent.InletName, out PipeNode? inlet))
                return;

            var ksInletPressure = inlet.Air.Pressure; // KS14
            var ksEnvironmentPressure = environment.Pressure; // KS14
            var inletAir = inlet.Air.RemoveRatio(1f, includeLiquid: false /* KS14: the liquid leaves below, by its own law */);
            var envAir = environment.RemoveRatio(1f);

            var mergeAir = new GasMixture(inletAir.Volume + envAir.Volume) { FluidCapable = true /* KS14: added */ };
            _atmosphereSystem.Merge(mergeAir, inletAir);
            _atmosphereSystem.Merge(mergeAir, envAir);

            _atmosphereSystem.Merge(inlet.Air, mergeAir.RemoveVolume(inletAir.Volume));
            _ksFluidmosTileSystem.ReleaseToTile(uid, mergeAir); // KS14
            _atmosphereSystem.Merge(environment, mergeAir);
            KsReleaseLiquid(uid, inlet.Air, ksInletPressure - ksEnvironmentPressure, args.dt); // KS14
        }

        // KS14 start: liquid leaves a vent by the orifice law, as it crosses a passive gate, pushed out by the pipe's
        //     pressure over the room's. Split by volume with the gas instead, most of it left in a single update, however
        //     little gas moved. The tile buffer gathers what leaves until there is a puddle's worth, so none is lost.
        private void KsReleaseLiquid(EntityUid uid, GasMixture pipeAir, float pressureDifference, float deltaTime)
        {
            if (pressureDifference <= 0f ||
                pipeAir.Fluid is not { } fluid ||
                fluid.LiquidUnits <= Content.Shared._KS14.Fluidmos.KsFluidmos.MinLiquidUnits)
                return;

            var units = MathF.Min(Content.Shared._KS14.Fluidmos.KsFluidmos.DefaultLiquidConductance * MathF.Sqrt(pressureDifference) * deltaTime,
                fluid.LiquidUnits);
            _ksFluidmosTileSystem.ReleaseToTile(uid, pipeAir.RemoveLiquidRatio(units / fluid.LiquidUnits));
        }
        // KS14 end
    }
}
