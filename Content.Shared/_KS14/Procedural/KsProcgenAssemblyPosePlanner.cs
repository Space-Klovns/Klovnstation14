using System.Linq;
using System.Numerics;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenFloorRootPose(string MemberId, Vector2 Position);
public sealed record KsProcgenMemberOrientation(string MemberId, int QuarterTurns);

/// <summary>
/// Proposed transforms in one common map/grid coordinate frame. A support parent is not necessarily
/// a transform parent: surface drops remain siblings, while container insertion reparents its child.
/// </summary>
public sealed record KsProcgenSupportedMemberPose(
    KsProcgenSupportMember Support, Vector2 Position, int QuarterTurns,
    string? CoordinateParentMemberId, Vector2 LocalPosition, int LocalQuarterTurns);

public sealed class KsProcgenAssemblyPosePlan
{
    public KsProcgenAssemblySupportStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenSupportedMemberPose> Members { get; init; } = [];
    public KsProcgenAssemblySupportPlan? SupportPlan { get; init; }
    public ulong PoseHash { get; init; }
    public bool EnginePlacementVerified => false;
}

/// <summary>
/// Bounded transform proposals for a selected variant, before collision, interaction or engine staging.
/// Rebuilds support metadata instead of accepting a stale support plan. Does not permit floor overlap.
/// </summary>
public static class KsProcgenAssemblyPosePlanner
{
    public static KsProcgenAssemblyPosePlan Plan(KsProcgenResolvedAssembly assembly,
        IReadOnlyCollection<string> selectedMemberIds, KsProcgenAssemblyCapabilityReport capabilities,
        IReadOnlyList<KsProcgenFloorRootPose> floorRoots,
        IReadOnlyList<KsProcgenMemberOrientation> orientations, int assemblyQuarterTurns)
    {
        if (assemblyQuarterTurns is < 0 or > 3 || floorRoots.Count is < 1 or > 64 ||
            orientations.Count is < 1 or > 64 ||
            floorRoots.Any(root => string.IsNullOrWhiteSpace(root.MemberId) || !ValidPosition(root.Position)) ||
            orientations.Any(orientation => string.IsNullOrWhiteSpace(orientation.MemberId) || orientation.QuarterTurns is < 0 or > 3) ||
            floorRoots.Select(root => root.MemberId).Distinct(StringComparer.Ordinal).Count() != floorRoots.Count ||
            orientations.Select(orientation => orientation.MemberId).Distinct(StringComparer.Ordinal).Count() != orientations.Count)
            return Failure("InvalidAssemblyPoseInput", invalidInput: true);
        var support = KsProcgenAssemblySupportPlanner.Plan(assembly, selectedMemberIds, capabilities);
        if (support.Status != KsProcgenAssemblySupportStatus.NeedsEngineValidation)
            return new KsProcgenAssemblyPosePlan { Status = support.Status, Issue = support.Issue };
        var roots = floorRoots.ToDictionary(root => root.MemberId, root => root.Position, StringComparer.Ordinal);
        var turns = orientations.ToDictionary(orientation => orientation.MemberId, orientation => orientation.QuarterTurns,
            StringComparer.Ordinal);
        if (roots.Count != support.Members.Count(member => member.Layer == KsProcgenPlacementLayer.Floor) ||
            support.Members.Any(member => member.Layer == KsProcgenPlacementLayer.Floor && !roots.ContainsKey(member.MemberId)) ||
            turns.Count != support.Members.Count || support.Members.Any(member => !turns.ContainsKey(member.MemberId)))
            return Failure("AssemblyPoseSelectionMismatch", invalidInput: true);
        var declarations = capabilities.Members.ToDictionary(member => member.MemberId, member => member.Capabilities,
            StringComparer.Ordinal);
        var definitions = assembly.Members.ToDictionary(member => member.Id, StringComparer.Ordinal);
        var ordered = new List<KsProcgenSupportedMemberPose>();
        var resolved = new Dictionary<string, KsProcgenSupportedMemberPose>(StringComparer.Ordinal);
        foreach (var member in support.Members)
        {
            var definition = definitions[member.MemberId];
            var turn = turns[member.MemberId];
            if (definition.Entry.AllowedQuarterTurns.Count is < 1 or > 4 ||
                definition.Entry.AllowedQuarterTurns.Any(allowed => allowed is < 0 or > 3) ||
                definition.LocalQuarterTurns is < 0 or > 3 ||
                !Enum.IsDefined(definition.RotationMode))
                return Failure("InvalidAssemblyPoseRotation", invalidInput: true);
            if (!definition.Entry.AllowedQuarterTurns.Contains(turn) ||
                definition.RotationMode == KsProcgenMemberRotation.AssemblyRelative &&
                turn != (assemblyQuarterTurns + definition.LocalQuarterTurns) % 4)
                return Failure("AssemblyPoseRotationNotAllowed");
            var parent = member.ParentMemberId == null ? null : resolved[member.ParentMemberId];
            var position = parent?.Position ?? roots[member.MemberId];
            string? coordinateParent = null;
            var localPosition = position;
            var localTurn = turn;
            if (member.Layer == KsProcgenPlacementLayer.Surface)
            {
                if (member.SlotId != null)
                    return Failure("AssemblyPoseNamedSurfaceUnsupported");
                if (!member.Exposed)
                    return Failure("AssemblyPoseContainedSurfaceUnsupported");
                var subject = declarations[member.MemberId];
                // A prototype's anchor flag is not the spawned state: placement off a grid can
                // produce an unanchored item. The owned adapter must check the live subject.
                if (!subject.HasItem)
                    return Failure("AssemblyPoseSurfaceSubjectUnsupported");
                var target = declarations[member.ParentMemberId!];
                var offset = target.SurfaceCentered ? target.SurfaceOffset : Vector2.Zero;
                if (!ValidPosition(offset) || Math.Abs(offset.X) > 16f || Math.Abs(offset.Y) > 16f)
                    return Failure("AssemblyPoseSurfaceOffsetOutOfRange");
                // PlaceableSurfaceSystem offsets the target's parent-frame coordinates, not its
                // rotated local axes. For a noncentered drop we choose the target origin as the click.
                position += offset;
                localPosition = position;
            }
            else if (member.Layer == KsProcgenPlacementLayer.Container)
            {
                // SharedContainerSystem.Insert establishes zero local position AND local rotation.
                if (turn != parent!.QuarterTurns)
                    return Failure("AssemblyPoseContainerRotationConflict");
                coordinateParent = member.ParentMemberId;
                localPosition = Vector2.Zero;
                localTurn = 0;
            }
            if (!ValidPosition(position))
                return Failure("AssemblyPosePositionOutOfRange");
            var pose = new KsProcgenSupportedMemberPose(member, position, turn, coordinateParent, localPosition, localTurn);
            ordered.Add(pose);
            resolved.Add(member.MemberId, pose);
        }
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-support-poses-v1");
        hash.AddInt(unchecked((int) support.StructureHash));
        hash.AddInt(unchecked((int) (support.StructureHash >> 32)));
        hash.AddInt(assemblyQuarterTurns);
        hash.AddInt(ordered.Count);
        foreach (var pose in ordered)
        {
            hash.AddString(pose.Support.MemberId);
            hash.AddInt(BitConverter.SingleToInt32Bits(pose.Position.X));
            hash.AddInt(BitConverter.SingleToInt32Bits(pose.Position.Y));
            hash.AddInt(pose.QuarterTurns);
            hash.AddString(pose.CoordinateParentMemberId ?? string.Empty);
            hash.AddInt(BitConverter.SingleToInt32Bits(pose.LocalPosition.X));
            hash.AddInt(BitConverter.SingleToInt32Bits(pose.LocalPosition.Y));
            hash.AddInt(pose.LocalQuarterTurns);
        }
        return new KsProcgenAssemblyPosePlan
        {
            Status = KsProcgenAssemblySupportStatus.NeedsEngineValidation,
            Members = ordered.AsReadOnly(), SupportPlan = support, PoseHash = hash.Value,
        };
    }

    private static bool ValidPosition(Vector2 position) => float.IsFinite(position.X) && float.IsFinite(position.Y) &&
        Math.Abs(position.X) <= 1_000_000f && Math.Abs(position.Y) <= 1_000_000f;

    private static KsProcgenAssemblyPosePlan Failure(string code, bool invalidInput = false) => new()
    {
        Status = invalidInput ? KsProcgenAssemblySupportStatus.InvalidInput : KsProcgenAssemblySupportStatus.Rejected,
        Issue = new KsProcgenIssue(code, "Assembly transform proposal cannot be accepted."),
    };
}
