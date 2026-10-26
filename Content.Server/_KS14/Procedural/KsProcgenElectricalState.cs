using System.Linq;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Procedural;

public sealed record KsProcgenElectricalConnectionState(string ConsumerId, EntityUid Consumer,
    EntityUid Provider, float ActualNominalWatts, float? DeclaredNominalWatts, float? DeclaredPeakWatts,
    int ProviderTransferRange, int ReceiverReceptionRange);

/// <summary>Observed paused native wiring at initialization. No delivered-power or stability promise.</summary>
public sealed class KsProcgenInitialElectricalReceipt
{
    internal KsProcgenInitialElectricalReceipt(ulong planHash, IEnumerable<KsProcgenElectricalConnectionState> connections,
        int cableCount, int deviceCount, int work, GameTick tick, TimeSpan time)
    {
        PlanHash = planHash; Connections = Array.AsReadOnly(connections.OrderBy(item => item.ConsumerId, StringComparer.Ordinal).ToArray());
        CableCount = cableCount; DeviceCount = deviceCount; Work = work; VerifiedAtTick = tick; VerifiedAtTime = time;
    }
    public ulong PlanHash { get; }
    public IReadOnlyList<KsProcgenElectricalConnectionState> Connections { get; }
    public int CableCount { get; }
    public int DeviceCount { get; }
    public int Work { get; }
    public GameTick VerifiedAtTick { get; }
    public TimeSpan VerifiedAtTime { get; }
    public bool ElectricalTopologyVerified => true;
    public bool ElectricalSupplyVerified => false;
    internal KsProcgenInitialElectricalReceipt WithWork(int work) => new(PlanHash, Connections, CableCount, DeviceCount,
        work, VerifiedAtTick, VerifiedAtTime);
}
