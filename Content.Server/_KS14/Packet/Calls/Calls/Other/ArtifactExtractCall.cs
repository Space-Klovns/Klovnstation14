using Content.Server._KS14.Packet.Components;
using Content.Shared.Xenoarchaeology.Equipment.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet.States.Calls;

public sealed class ArtifactExtractCall : DeviceCall
{
    protected override void GetCall(EntityUid entity, PacketSystem packetSystem, EntityManager entityMan, IPrototypeManager protoMan, PacketExecutorComponent executor)
    {
        if (!entityMan.TryGetComponent<AnalysisConsoleComponent>(entity, out var analyzerComponent))
            return;

        packetSystem.ArtifactAnalyzerSystem.Extract((entity, analyzerComponent));
    }
}
