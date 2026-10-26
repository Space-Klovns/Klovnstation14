using System.Linq;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenDiagnosticPoint(float X, float Y);
public sealed record KsProcgenDiagnosticPath(IReadOnlyList<KsProcgenDiagnosticCell> Cells, bool Truncated);
public sealed record KsProcgenDiagnosticSupportedEntity(string RegionId, KsProcgenDiagnosticMember Member,
    string EntityId, string Role, string Movement, string Layer, KsProcgenDiagnosticPoint? Position, int QuarterTurns,
    string? SupportParentMemberId, string? CoordinateParentMemberId, KsProcgenDiagnosticPoint LocalPosition, int LocalQuarterTurns,
    string? SlotId, bool Exposed, KsProcgenDiagnosticPath Footprint)
{
    public string PositionFrame => "RequestTiles";
}
public sealed record KsProcgenDiagnosticOperatingAccess(string RegionId, KsProcgenDiagnosticMember Member,
    string Layer, int QuarterTurns, KsProcgenDiagnosticCell Approach, string? AssociatedSeatMemberId, KsProcgenDiagnosticPath CleanPath);
public sealed record KsProcgenDiagnosticRelation(string RegionId, string RelationId, KsProcgenDiagnosticMember Subject,
    KsProcgenDiagnosticMember? Target, string Kind, string Severity, string State, string? ReasonCode,
    int? MinimumDistance, int? MaximumDistance, int? PathDistance, KsProcgenDiagnosticPath CleanPath);

internal sealed class KsProcgenSupportedReviewContext(string regionId, KsProcgenLayeredFurnishing layered, KsProcgenDiagnosticProjectionBudget budget)
{
    internal readonly string RegionId = regionId;
    internal readonly KsProcgenLayeredFurnishing Layered = layered;
    private readonly Dictionary<string, KsProcgenRoomMemberBinding> _bindings = budget.Read(layered.Bindings).ToDictionary(binding => binding.MemberId, StringComparer.Ordinal);
    private readonly Dictionary<string, KsProcgenResolvedAssemblyMember> _definitions = budget.Read(layered.Assembly.Members).ToDictionary(member => member.Id, StringComparer.Ordinal);
    private readonly Dictionary<string, KsProcgenSupportedFootprint> _footprints = budget.Read(layered.Access.Footprints).ToDictionary(footprint => footprint.MemberId, StringComparer.Ordinal);
    internal KsProcgenDiagnosticMember Member(string id)
    {
        var binding = _bindings[id];
        return new(id, binding.PackId, binding.CoreId, binding.CopyIndex, binding.AssemblyId, binding.VariantId,
            binding.SourceMemberId, binding.Origin.ToString());
    }
    internal KsProcgenDiagnosticSupportedEntity Entity(KsProcgenSupportedMemberPose pose, KsProcgenDiagnosticProjectionBudget budget)
    {
        var entry = _definitions[pose.Support.MemberId].Entry;
        var position = pose.Position - Layered.Room.CellCenterOrigin;
        return new(RegionId, Member(pose.Support.MemberId), entry.Entity, entry.Role.ToString(), entry.Movement.ToString(),
            pose.Support.Layer.ToString(), pose.Support.Exposed ? new(position.X + 0.5f, position.Y + 0.5f) : null, pose.QuarterTurns,
            pose.Support.ParentMemberId, pose.CoordinateParentMemberId, new(pose.LocalPosition.X, pose.LocalPosition.Y), pose.LocalQuarterTurns,
            pose.Support.SlotId, pose.Support.Exposed,
            budget.Geometry(_footprints.TryGetValue(pose.Support.MemberId, out var footprint) ? footprint.Cells : []));
    }
}
