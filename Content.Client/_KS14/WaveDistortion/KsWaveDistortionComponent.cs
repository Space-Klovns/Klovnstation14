using Content.Shared._KS14.WaveDistortion;

namespace Content.Client._KS14.WaveDistortion;

/*
    The original version of this source code was ported from
        https://github.com/crystallpunk-14/crystall-punk-14/ at commit 5b6108377e40235c768be3ac6ffadb37a085f441
*/

[RegisterComponent]
[Access(typeof(KsWaveDistortionSystem))]
public sealed partial class KsWaveDistortionComponent : Component
{
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Speed = 10f;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Distortion = 10f;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Offset = 0f;
}
