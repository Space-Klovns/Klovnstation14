using System.Linq;
using Content.Shared._KS14.Procedural;
using Content.Shared.Wall;
using Robust.Shared.Maths;

namespace Content.Server._KS14.Procedural;

/// <summary>Layout inputs captured independently of the electrical router's service witness.</summary>
internal sealed class KsProcgenOwnedElectricalLayout
{
    internal sealed record Region(string Id, KsProcgenZoneKind Kind, IReadOnlyList<Vector2i> Floor,
        IReadOnlyList<Vector2i> Protected, KsProcgenUtilityRoomReservation? UtilityRoom);

    internal KsProcgenOwnedElectricalLayout(IEnumerable<KsProcgenThemedRegion> regions,
        IEnumerable<KsProcgenPartitionDoor> doors)
    {
        Regions = Array.AsReadOnly(regions.Select(region => new Region(region.Id, region.Kind,
            Array.AsReadOnly(region.FloorCells.ToArray()), Array.AsReadOnly(region.ProtectedPassageCells.ToArray()),
            region.UtilityRoom)).ToArray());
        Doors = Array.AsReadOnly(doors.Select(door => door with { }).ToArray());
    }

    internal IReadOnlyList<Region> Regions { get; }
    internal IReadOnlyList<KsProcgenPartitionDoor> Doors { get; }
}

public sealed partial class KsProcgenOwnedElectricalSystem
{
    /// <summary>Bind routed declarations to author-selected policy and physically inspected scene members.</summary>
    internal string? InspectPolicyAndDevices(KsProcgenOwnedStage stage, KsProcgenElectricalProfile declaredProfile,
        IReadOnlyList<KsProcgenElectricalDevicePlacement> declaredDevices, Func<bool> spend)
    {
        if (stage.Scene.ElectricalProfile is not { } original || original.SemanticHash != declaredProfile.SemanticHash)
            return "OwnedElectricalPolicyAuthorityDrift";
        var expected = new Dictionary<string, (KsProcgenElectricalDevicePlacement Device, KsProcgenSupportedMemberPose Pose, EntityUid Uid)>(StringComparer.Ordinal);
        for (var index = 0; index < stage.Scene.Assemblies.Count; index++)
        {
            if (!spend()) return "OwnedElectricalWorkBudget";
            var assembly = stage.Scene.Assemblies[index];
            var poses = new Dictionary<string, KsProcgenSupportedMemberPose>(StringComparer.Ordinal);
            foreach (var pose in assembly.Poses.Members)
            {
                if (!spend()) return "OwnedElectricalWorkBudget";
                poses.Add(pose.Support.MemberId, pose);
            }
            foreach (var binding in assembly.Bindings)
            {
                if (!spend()) return "OwnedElectricalWorkBudget";
                if (binding.Origin != KsProcgenRoomMemberOrigin.Infrastructure) continue;
                if (binding.CoreId is not ("Apc" or "Substation") || !poses.TryGetValue(binding.MemberId, out var pose) ||
                    !stage.Assemblies[index].TryGetValue(binding.MemberId, out var uid) ||
                    pose.Support.Layer is not (KsProcgenPlacementLayer.Floor or KsProcgenPlacementLayer.Wall))
                    return "OwnedElectricalConverterAuthorityDrift";
                var id = assembly.RegionId + "/" + binding.MemberId;
                var cell = new Vector2i((int) Math.Floor((double) pose.Position.X), (int) Math.Floor((double) pose.Position.Y));
                var device = new KsProcgenElectricalDevicePlacement(id, assembly.RegionId, pose.Support.EntityPrototypeId,
                    cell, pose.QuarterTurns, binding.CoreId == "Substation", pose.Support.Layer == KsProcgenPlacementLayer.Wall);
                if (!expected.TryAdd(id, (device, pose, uid))) return "OwnedElectricalConverterAuthorityDrift";
            }
        }
        if (declaredDevices.Count != expected.Count) return "OwnedElectricalConverterAuthorityDrift";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var device in declaredDevices)
        {
            if (!spend()) return "OwnedElectricalWorkBudget";
            if (!seen.Add(device.Id) || !expected.TryGetValue(device.Id, out var source) || device != source.Device)
                return "OwnedElectricalConverterAuthorityDrift";
            if (!TryComp(source.Uid, out TransformComponent? transform) || !transform.Anchored ||
                transform.ParentUid != stage.GridUid || transform.LocalPosition != source.Pose.Position ||
                transform.LocalRotation != KsProcgenRotation.ToEngineAngle(source.Pose.QuarterTurns) ||
                !TryComp(source.Uid, out MetaDataComponent? metadata) || metadata.EntityPrototype?.ID != source.Device.PrototypeId ||
                HasComp<WallMountComponent>(source.Uid) != source.Device.WallMounted)
                return "OwnedElectricalConverterPhysicalDrift";
        }
        return null;
    }

    /// <summary>
    /// Rebuild policy domains from the separately captured scene, never from the witness being
    /// checked. The caller first verifies the native scene and the policy/device claims.
    /// </summary>
    internal string? InspectServiceLayout(KsProcgenOwnedStage stage, IReadOnlyList<Vector2i> declaredAllowed,
        IReadOnlyList<Vector2i> declaredMediumTargets, IReadOnlyList<Vector2i> declaredFloor,
        IReadOnlyList<KsProcgenElectricalUtilityRoom> declaredRooms, Func<bool> spend)
    {
        if (stage.Scene.ElectricalProfile?.Distribution is not { } policy || stage.Scene.ElectricalLayout is not { } layout)
            return "OwnedElectricalServiceLayoutMissing";
        var allowed = new HashSet<Vector2i>();
        var writableCableCells = stage.Request.Utilities?.CableWriteCells;
        HashSet<Vector2i>? mask = null;
        if (writableCableCells != null)
        {
            mask = new();
            foreach (var cell in writableCableCells)
            {
                if (!spend()) return "OwnedElectricalWorkBudget";
                mask.Add(cell);
            }
        }
        foreach (var tile in stage.Scene.Tiles)
        {
            if (!spend()) return "OwnedElectricalWorkBudget";
            if (mask == null || mask.Contains(tile.Cell)) allowed.Add(tile.Cell);
        }
        var medium = new HashSet<Vector2i>();
        foreach (var assembly in stage.Scene.Assemblies)
        {
            var poses = new Dictionary<string, KsProcgenSupportedMemberPose>(StringComparer.Ordinal);
            foreach (var pose in assembly.Poses.Members)
            {
                if (!spend()) return "OwnedElectricalWorkBudget";
                poses.Add(pose.Support.MemberId, pose);
            }
            foreach (var binding in assembly.Bindings)
            {
                if (!spend()) return "OwnedElectricalWorkBudget";
                if (binding.Origin != KsProcgenRoomMemberOrigin.Infrastructure) continue;
                if (!poses.TryGetValue(binding.MemberId, out var pose)) return "OwnedElectricalConverterAuthorityDrift";
                medium.Add(new((int) Math.Floor((double) pose.Position.X), (int) Math.Floor((double) pose.Position.Y)));
            }
        }
        var floor = new HashSet<Vector2i>();
        var rooms = new Dictionary<string, KsProcgenUtilityRoomReservation>(StringComparer.Ordinal);
        foreach (var region in layout.Regions)
        {
            if (!spend()) return "OwnedElectricalWorkBudget";
            if (region.UtilityRoom is { } utility)
            {
                rooms.Add(region.Id, utility);
                if (policy.MediumVoltageCirculationBackbone)
                {
                    if (!spend() || !spend() || !spend()) return "OwnedElectricalWorkBudget";
                    medium.Add(utility.InsideApproach); medium.Add(utility.Threshold); medium.Add(utility.OutsideApproach);
                }
            }
            if (policy.MediumVoltageCirculationBackbone)
            foreach (var cell in region.Protected)
            {
                if (!spend()) return "OwnedElectricalWorkBudget";
                medium.Add(cell);
            }
            int? medianX = null; int? medianY = null;
            if (policy.MediumVoltageRoomEntryBranches && region.Kind == KsProcgenZoneKind.RoomProposal && region.Floor.Count > 0)
            {
                // Medians retain repeated coordinates: a wide row contributes each of its cells.
                foreach (var unused in region.Floor)
                    if (!spend()) return "OwnedElectricalWorkBudget";
                medianX = region.Floor.OrderBy(cell => cell.X).ElementAt(region.Floor.Count / 2).X;
                medianY = region.Floor.OrderBy(cell => cell.Y).ElementAt(region.Floor.Count / 2).Y;
            }
            foreach (var cell in region.Floor)
            {
                if (!spend()) return "OwnedElectricalWorkBudget";
                if (!stage.Scene.Floor.Contains(cell)) return "OwnedElectricalServiceLayoutFloorDrift";
                if (region.Kind == KsProcgenZoneKind.Passage && policy.MediumVoltageCirculationBackbone ||
                    cell.X == medianX || cell.Y == medianY) medium.Add(cell);
                if (region.Kind == KsProcgenZoneKind.RoomProposal &&
                    (policy.FloorServiceDomain == KsProcgenElectricalFloorDomain.AllRoomFloor || !stage.Scene.Blocked.Contains(cell)))
                    floor.Add(cell);
            }
        }
        if (policy.MediumVoltageRoomEntryBranches)
        foreach (var door in layout.Doors)
        {
            if (!spend() || !spend() || !spend()) return "OwnedElectricalWorkBudget";
            medium.Add(door.InsideApproach); medium.Add(door.Threshold); medium.Add(door.OutsideApproach);
        }
        string? Match(HashSet<Vector2i> expected, IReadOnlyList<Vector2i> actual, string code)
        {
            if (actual.Count != expected.Count) return code;
            var unique = new HashSet<Vector2i>();
            foreach (var cell in actual)
            {
                if (!spend()) return "OwnedElectricalWorkBudget";
                if (!expected.Contains(cell) || !unique.Add(cell)) return code;
            }
            return null;
        }
        var issue = Match(allowed, declaredAllowed, "OwnedElectricalCableDomainDrift") ??
            Match(medium, declaredMediumTargets, "OwnedElectricalMediumTargetDomainDrift") ??
            Match(floor, declaredFloor, "OwnedElectricalFloorServiceDomainDrift");
        if (issue != null) return issue;
        if (declaredRooms.Count != rooms.Count) return "OwnedElectricalUtilityRoomDomainDrift";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var room in declaredRooms)
        {
            if (!spend()) return "OwnedElectricalWorkBudget";
            if (!seen.Add(room.RegionId) || !rooms.TryGetValue(room.RegionId, out var original))
                return "OwnedElectricalUtilityRoomDomainDrift";
            var reservation = room.Reservation;
            if (reservation.ThemeId != original.ThemeId || reservation.Minimum != original.Minimum ||
                reservation.Size != original.Size || reservation.Threshold != original.Threshold ||
                reservation.InsideApproach != original.InsideApproach || reservation.OutsideApproach != original.OutsideApproach ||
                !reservation.ReusedHullCells.SequenceEqual(original.ReusedHullCells))
                return "OwnedElectricalUtilityRoomDomainDrift";
        }
        return null;
    }
}
