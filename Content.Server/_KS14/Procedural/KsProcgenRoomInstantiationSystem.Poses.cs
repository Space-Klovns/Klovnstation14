using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenRoomInstantiationSystem
{
    /// <summary>Translation-invariant authored mounting checks only; no live collision or support claim.</summary>
    internal bool TryCheckPlacement(KsProcgenRoomEntityExtraction extraction, string roomId, Vector2i origin,
        int quarterTurns, int maximumEntityProbes, out int entityProbes, out KsProcgenIssue? issue,
        CancellationToken cancellationToken = default)
    {
        entityProbes = 0; issue = null;
        if (extraction == null || quarterTurns is < 0 or > 3 || maximumEntityProbes is < 0 or > 65_536 ||
            origin.X is < -1_000_000 or > 1_000_000 || origin.Y is < -1_000_000 or > 1_000_000)
            return Reject("InvalidRoomPlacementInput", "Mounting checks require bounded captured inputs.", out issue);
        var room = extraction.Rooms.SingleOrDefault(room => room.Room.RoomAssetId == roomId);
        if (room == null) return Reject("RoomInstantiationMissingRoom", "No captured room has the requested identity.", out issue);
        foreach (var entity in room.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entityProbes >= maximumEntityProbes)
                return Reject("RoomPlacementProbeBudget", "Mounting checks exceeded their shared entity allowance.", out issue);
            entityProbes++;
            if (!TryPose(entity, room.Room.GridYamlUid, origin, quarterTurns, out _, out _, out issue)) return false;
        }
        return true;
    }

    private static bool TryPose(KsProcgenExtractedMapEntity entity, int gridYamlUid, Vector2i origin, int quarterTurns,
        out Vector2 position, out double rotation, out KsProcgenIssue? issue)
    {
        // Rotate about the center of cell (0,0); descendants keep their parent-local poses.
        var root = entity.ParentYamlUid == gridYamlUid;
        position = root ? RotatePoint(entity.RoomPosition, quarterTurns) + new Vector2(origin.X, origin.Y) : entity.LocalPosition;
        rotation = entity.LocalRotationRadians + (root ? KsProcgenRotation.ToEngineAngle(quarterTurns).Theta : 0.0);
        if (entity.Anchored && (!root || MathF.Abs(position.X - MathF.Floor(position.X) - 0.5f) > 0.00001f ||
                MathF.Abs(position.Y - MathF.Floor(position.Y) - 0.5f) > 0.00001f))
            return Reject("RoomInstantiationAnchoredPoseUnsupported", "Anchored members must be grid roots at exact tile centers.", out issue);
        if (entity.NoLocalRotation && rotation != 0.0)
            return Reject("RoomInstantiationRotationUnsupported", "The placement rotates a member that forbids local rotation.", out issue);
        issue = null; return true;
    }
}
