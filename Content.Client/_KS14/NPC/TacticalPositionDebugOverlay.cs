using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._KS14.NPC;

/// <summary>
/// Draws the most recent tactical position debug frame per NPC: every scored candidate (colored red-to-green
/// by score), the chosen candidate (highlighted), and live claim-table entries (clearance-radius circles).
/// </summary>
public sealed partial class TacticalPositionDebugOverlay : Overlay
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private TacticalPositionDebugSystem _system = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private const float CandidateRadius = 0.12f;
    private const float ChosenRadius = 0.2f;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var worldHandle = args.WorldHandle;

        foreach (var (_, frame) in _system.Frames)
        {
            var data = frame.Data;
            var maxScore = 0.0001f;

            foreach (var candidate in data.Candidates)
            {
                maxScore = MathF.Max(maxScore, candidate.Score);
            }

            foreach (var claim in data.Claims)
            {
                var claimCoordinates = _entityManager.GetCoordinates(claim.Coordinates);
                var claimMapPosition = _transformSystem.ToMapCoordinates(claimCoordinates);

                if (claimMapPosition.MapId != args.MapId)
                    continue;

                worldHandle.DrawCircle(claimMapPosition.Position, claim.ClearanceRadius, Color.Orange.WithAlpha(0.5f), false);
            }

            foreach (var candidate in data.Candidates)
            {
                var candidateCoordinates = _entityManager.GetCoordinates(candidate.Coordinates);
                var candidateMapPosition = _transformSystem.ToMapCoordinates(candidateCoordinates);

                if (candidateMapPosition.MapId != args.MapId)
                    continue;

                var color = Color.InterpolateBetween(Color.Red, Color.Lime, candidate.Score / maxScore).WithAlpha(0.65f);
                worldHandle.DrawCircle(candidateMapPosition.Position, CandidateRadius, color);
            }

            if (data.Chosen is { } chosen)
            {
                var chosenCoordinates = _entityManager.GetCoordinates(chosen);
                var chosenMapPosition = _transformSystem.ToMapCoordinates(chosenCoordinates);

                if (chosenMapPosition.MapId == args.MapId)
                {
                    worldHandle.DrawCircle(chosenMapPosition.Position, ChosenRadius, Color.Yellow.WithAlpha(0.9f), false);
                }
            }
        }
    }
}
