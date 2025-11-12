using Content.Server.Atmos.Piping.Trinary.EntitySystems;
using Content.Shared._KS14.Atmos;
using Content.Server._KS14.Atmos.Piping.Trinary.EntitySystems;

namespace Content.Server._KS14.Atmos.Piping.Trinary.Components
{
    [RegisterComponent]
    [Access(typeof(MolarMixerSystem))]
    public sealed partial class MolarMixerComponent : Component
    {
        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("enabled")]
        public bool Enabled = true;

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("inletOne")]
        public string InletOneName = "inletOne";

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("inletTwo")]
        public string InletTwoName = "inletTwo";

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("outlet")]
        public string OutletName = "outlet";

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("targetMolarFlow")]
        public float TargetMolarFlow = 1f;

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("maxTargetMolarFlow")]
        public float MaxTargetMolarFlow = KsAtmospherics.MaxMolarFlow;

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("inletOneConcentration")]
        public float InletOneConcentration = 0.5f;

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("inletTwoConcentration")]
        public float InletTwoConcentration = 0.5f;
    }
}
