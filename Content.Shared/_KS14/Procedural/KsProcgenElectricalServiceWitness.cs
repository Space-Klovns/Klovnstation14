using System.Linq;
using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenElectricalUtilityRoom(string RegionId, KsProcgenUtilityRoomReservation Reservation);

/// <summary>Immutable service intent and measured geometry, separate from actual power or membership.</summary>
public sealed class KsProcgenElectricalServiceWitness
{
    internal KsProcgenElectricalServiceWitness(IEnumerable<Vector2i> allowed, IEnumerable<Vector2i> mediumTargets,
        IEnumerable<Vector2i> floorDomain, IEnumerable<Vector2i> covered, int requiredFloorCells, int providerRange,
        IEnumerable<KsProcgenLvSquareProof> proofs, IEnumerable<KsProcgenElectricalUtilityRoom> rooms)
    {
        AllowedCableCells = KsProcgenGeometry.SortCells(allowed); MediumVoltageTargets = KsProcgenGeometry.SortCells(mediumTargets);
        FloorDomain = KsProcgenGeometry.SortCells(floorDomain); CoveredFloorCells = KsProcgenGeometry.SortCells(covered);
        var coveredSet = CoveredFloorCells.ToHashSet();
        UncoveredFloorCells = KsProcgenGeometry.SortCells(FloorDomain.Where(cell => !coveredSet.Contains(cell)));
        RequiredFloorCells = requiredFloorCells; ProviderRange = providerRange;
        SquareProofs = KsProcgenReadOnly.Copy(proofs); Rooms = KsProcgenReadOnly.Copy(rooms.OrderBy(room => room.RegionId, StringComparer.Ordinal));
        var hash = KsProcgenStableHash.Create(); hash.AddString("ks-electrical-service-v1");
        foreach (var cells in new[] { AllowedCableCells, MediumVoltageTargets, FloorDomain, CoveredFloorCells })
            hash.AddString(KsProcgenLowVoltageRouter.CellHash(cells).ToString(System.Globalization.CultureInfo.InvariantCulture));
        hash.AddInt(RequiredFloorCells); hash.AddInt(ProviderRange); hash.AddInt(Rooms.Count);
        foreach (var room in Rooms) { hash.AddString(room.RegionId); room.Reservation.AddHash(ref hash); }
        hash.AddInt(SquareProofs.Count);
        foreach (var proof in SquareProofs)
        {
            hash.AddInt(proof.Minimum.X); hash.AddInt(proof.Minimum.Y);
            foreach (var cell in proof.MandatoryTerminals) { hash.AddInt(cell.X); hash.AddInt(cell.Y); }
            hash.AddString(proof.CandidateDomainHash.ToString(System.Globalization.CultureInfo.InvariantCulture));
            hash.AddString(proof.TerminalHash.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        DeclarationHash = hash.Value;
    }
    public IReadOnlyList<Vector2i> AllowedCableCells { get; }
    public IReadOnlyList<Vector2i> MediumVoltageTargets { get; }
    public IReadOnlyList<Vector2i> FloorDomain { get; }
    public IReadOnlyList<Vector2i> CoveredFloorCells { get; }
    public IReadOnlyList<Vector2i> UncoveredFloorCells { get; }
    public int RequiredFloorCells { get; }
    public int ProviderRange { get; }
    public IReadOnlyList<KsProcgenLvSquareProof> SquareProofs { get; }
    public IReadOnlyList<KsProcgenElectricalUtilityRoom> Rooms { get; }
    public ulong DeclarationHash { get; }

    public bool Validate(KsProcgenElectricalDistribution distribution, IReadOnlyList<KsProcgenElectricalCable> cables,
        IReadOnlyList<KsProcgenElectricalDevicePlacement> devices, Func<bool> spend, out string? issue)
    {
        issue = null;
        var allowed = AllowedCableCells.ToHashSet(); var medium = new HashSet<Vector2i>(); var low = new HashSet<Vector2i>();
        foreach (var cable in cables)
        {
            if (!spend()) { issue = "ElectricalServiceWorkBudget"; return false; }
            if (!allowed.Contains(cable.Cell)) { issue = "ElectricalServiceCablePermissionDrift"; return false; }
            if (cable.Voltage == KsProcgenElectricalVoltage.Low) low.Add(cable.Cell);
            if (cable.Voltage == KsProcgenElectricalVoltage.Medium) medium.Add(cable.Cell);
        }
        foreach (var cell in MediumVoltageTargets)
        {
            if (!spend()) { issue = "ElectricalServiceWorkBudget"; return false; }
            if (!medium.Contains(cell)) { issue = "ElectricalMediumServiceUnmet"; return false; }
        }
        var covered = new HashSet<Vector2i>();
        foreach (var cell in FloorDomain)
        foreach (var provider in low)
        {
            if (!spend()) { issue = "ElectricalServiceWorkBudget"; return false; }
            if (!KsProcgenElectricalReach.WithinRange(Center(provider), Center(cell), ProviderRange, ProviderRange)) continue;
            covered.Add(cell); break;
        }
        if (!covered.SetEquals(CoveredFloorCells) || RequiredFloorCells != (int) Math.Ceiling((double) ((float) FloorDomain.Count * distribution.FloorServiceFraction)) ||
            covered.Count < RequiredFloorCells)
        { issue = "ElectricalFloorServiceUnmet"; return false; }
        var terminals = devices.Where(device => !device.Substation).Select(device => device.Cell).ToHashSet();
        var domainHash = KsProcgenLowVoltageRouter.CellHash(allowed); var terminalHash = KsProcgenLowVoltageRouter.CellHash(terminals);
        var blocks = new HashSet<Vector2i>();
        foreach (var minimum in KsProcgenLowVoltageRouter.SquareMinima(low))
        {
            if (!spend()) { issue = "ElectricalServiceWorkBudget"; return false; }
            if (!KsProcgenLowVoltageRouter.Square(minimum).All(low.Contains)) continue;
            blocks.Add(minimum);
            var matches = SquareProofs.Where(proof => proof.Minimum == minimum).ToArray();
            if (distribution.LowVoltageSquares == KsProcgenElectricalSquarePolicy.ForbidNew || matches.Length != 1 ||
                matches[0].CandidateDomainHash != domainHash || matches[0].TerminalHash != terminalHash ||
                !matches[0].MandatoryTerminals.ToHashSet().SetEquals(KsProcgenLowVoltageRouter.Square(minimum)) ||
                !matches[0].MandatoryTerminals.All(terminals.Contains))
            { issue = "ElectricalLvSquareProofRequired"; return false; }
        }
        if (SquareProofs.Count != blocks.Count || SquareProofs.Any(proof => !blocks.Contains(proof.Minimum)))
        { issue = "ElectricalLvSquareProofDrift"; return false; }
        if (Rooms.Count == 0 || Rooms.Select(room => room.RegionId).Distinct(StringComparer.Ordinal).Count() != Rooms.Count ||
            devices.Count(device => device.Substation) != Rooms.Count)
        { issue = "ElectricalUtilityRoomDeviceDrift"; return false; }
        foreach (var room in Rooms)
        {
            if (!spend()) { issue = "ElectricalServiceWorkBudget"; return false; }
            var substations = devices.Where(device => device.Substation && device.RegionId == room.RegionId).ToArray();
            if (substations.Length != 1 || devices.Any(device => !device.Substation && device.RegionId == room.RegionId) ||
                !(substations[0].WallMounted ? room.Reservation.EnclosureCells : room.Reservation.InteriorCells).Contains(substations[0].Cell) ||
                substations[0].Cell == room.Reservation.Threshold)
            { issue = "ElectricalUtilityRoomDeviceDrift"; return false; }
        }
        return true;
    }

    private static Vector2 Center(Vector2i cell) => new((float) cell.X + 0.5f, (float) cell.Y + 0.5f);
}
