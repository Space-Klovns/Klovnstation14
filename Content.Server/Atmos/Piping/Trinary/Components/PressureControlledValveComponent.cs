using Content.Shared.Atmos;
using Content.Shared.Guidebook;

namespace Content.Server.Atmos.Piping.Trinary.Components
{
    [RegisterComponent]
    public sealed partial class PressureControlledValveComponent : Component
    {
        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("inlet")]
        public string InletName { get; set; } = "inlet";

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("control")]
        public string ControlName { get; set; } = "control";

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("outlet")]
        public string OutletName { get; set; } = "outlet";

        [ViewVariables(VVAccess.ReadOnly)]
        [DataField("enabled")]
        public bool Enabled { get; set; } = false;

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("gain")]
        public float Gain { get; set; } = 10;

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("threshold")]
        [GuidebookData]
        public float Threshold { get; set; } = Atmospherics.OneAtmosphere;

        [DataField("maxTransferRate")]
        public float MaxTransferRate { get; set; } = Atmospherics.MaxTransferRate;

        // KS14 start
        /// <summary>
        ///     KS14: units of liquid per second per square root kPa that cross this device by the orifice law, apart from its gas. Scaled by how
        ///         far the valve is open.
        /// </summary>
        [DataField]
        public float KsLiquidConductance = Content.Shared._KS14.Fluidmos.KsFluidmos.DefaultLiquidConductance;
        // KS14 end
    }
}
