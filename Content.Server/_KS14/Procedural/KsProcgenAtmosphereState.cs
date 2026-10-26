using System.Linq;
using Content.Shared._KS14.Procedural;
using Content.Shared.Atmos;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Procedural;

/// <summary>Detached observation of one actual grid tile; no writable mixture escapes the atmosphere API.</summary>
public sealed record KsProcgenAtmosCellState(Vector2i Cell, bool OwnedMixture, bool Airless,
    AtmosDirection OpenDirections, float VolumeLiters, float TemperatureKelvin, float PressureKPa,
    IReadOnlyList<KsProcgenGasAmount> Gases);

internal sealed record KsProcgenAtmosWrite(Vector2i Cell, KsProcgenAtmosphereProfile Profile);

/// <summary>Evidence at initialization, not a promise about later simulation or gameplay.</summary>
public sealed class KsProcgenInitialAtmosphereReceipt
{
    internal KsProcgenInitialAtmosphereReceipt(ulong bindingHash, IEnumerable<KsProcgenAtmosCellState> cells,
        GameTick verifiedAtTick, TimeSpan verifiedAtTime, IEnumerable<KsProcgenGasOmission> omissions)
    {
        BindingHash = bindingHash;
        VerifiedAtTick = verifiedAtTick; VerifiedAtTime = verifiedAtTime;
        Cells = Array.AsReadOnly(cells.Select(cell => cell with { Gases = Array.AsReadOnly(cell.Gases.ToArray()) }).ToArray());
        Omissions = Array.AsReadOnly(omissions.Distinct().OrderBy(omission => omission.AssignmentId, StringComparer.Ordinal)
            .ThenBy(omission => omission.DomainId, StringComparer.Ordinal).ThenBy(omission => omission.RoomId, StringComparer.Ordinal)
            .ThenBy(omission => omission.Code, StringComparer.Ordinal).ToArray());
    }
    public ulong BindingHash { get; }
    public GameTick VerifiedAtTick { get; }
    public TimeSpan VerifiedAtTime { get; }
    public IReadOnlyList<KsProcgenAtmosCellState> Cells { get; }
    public IReadOnlyList<KsProcgenGasOmission> Omissions { get; }
    public bool RequiredInitialGasVerified => true;
    /// <summary>All requested initial values, including preferences, passed; stability is a separate observation.</summary>
    public bool InitialGasVerified => Omissions.All(omission => omission.Code == "PreferredGasStabilityUnsupported");
    public bool AtmosphereAtRestVerified => false;
}
