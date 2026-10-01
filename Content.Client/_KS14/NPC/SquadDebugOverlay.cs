using System.Numerics;
using Content.Shared._KS14.NPC;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client._KS14.NPC;

/// <summary>
///     Draws every NPC squad in its own colour: a double ring on the leader, single rings on members, lines
///         from the leader to each member, squares on the room thresholds the squad is covering, and a line
///         from each member to its assigned cover position. The squad's threat is a cross. What each member believes
///         about the hostiles it knows of is drawn too - see <see cref="DrawContact"/>.
/// </summary>
public sealed partial class SquadDebugOverlay : Overlay
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private SquadDebugSystem _squadDebugSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private const float LeaderRadius = 0.55f;
    private const float MemberRadius = 0.4f;
    private const float ThresholdHalfSize = 0.3f;
    private const float CoverRadius = 0.15f;
    private const float ThreatHalfSize = 0.35f;
    private const float ContactRadius = 0.3f;

    private static readonly Color VisibleContactColor = Color.Red.WithAlpha(0.6f);
    private static readonly Color LostContactColor = Color.Orange;
    private static readonly Color ReportedContactColor = Color.Yellow;
    private static readonly Color HiddenContactColor = Color.Magenta;

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_squadDebugSystem.Latest is not { } data)
            return;

        var worldHandle = args.WorldHandle;

        foreach (var squad in data.Squads)
        {
            var color = GetSquadColor(squad.Squad);
            Vector2? leaderPosition = null;

            if (squad.Leader is { } leader && TryGetPosition(leader, args.MapId, out var leaderMapPosition))
            {
                leaderPosition = leaderMapPosition;
                worldHandle.DrawCircle(leaderMapPosition, LeaderRadius, color, false);
                worldHandle.DrawCircle(leaderMapPosition, LeaderRadius - 0.08f, color, false);
            }

            foreach (var member in squad.Members)
            {
                if (!TryGetPosition(member, args.MapId, out var memberPosition))
                    continue;

                worldHandle.DrawCircle(memberPosition, MemberRadius, color, false);

                if (leaderPosition is { } fromPosition)
                    worldHandle.DrawLine(fromPosition, memberPosition, color.WithAlpha(0.5f));
            }

            foreach (var threshold in squad.Thresholds)
            {
                if (!TryGetPosition(threshold, args.MapId, out var thresholdPosition))
                    continue;

                var halfSize = new Vector2(ThresholdHalfSize, ThresholdHalfSize);
                worldHandle.DrawRect(new Box2(thresholdPosition - halfSize, thresholdPosition + halfSize), color, false);
            }

            if (squad.Threat is { } threat && TryGetPosition(threat, args.MapId, out var threatPosition))
            {
                worldHandle.DrawLine(threatPosition - new Vector2(ThreatHalfSize, ThreatHalfSize), threatPosition + new Vector2(ThreatHalfSize, ThreatHalfSize), color);
                worldHandle.DrawLine(threatPosition - new Vector2(ThreatHalfSize, -ThreatHalfSize), threatPosition + new Vector2(ThreatHalfSize, -ThreatHalfSize), color);
            }

            foreach (var assignment in squad.Assignments)
            {
                if (!TryGetPosition(assignment.Member, args.MapId, out var memberPosition) ||
                    !TryGetPosition(assignment.Cover, args.MapId, out var coverPosition))
                    continue;

                worldHandle.DrawLine(memberPosition, coverPosition, color.WithAlpha(0.3f));
                worldHandle.DrawCircle(coverPosition, CoverRadius, color.WithAlpha(0.8f));
            }

            foreach (var contact in squad.Contacts)
            {
                DrawContact(worldHandle, contact, args.MapId);
            }
        }
    }

    /// <summary>
    ///     A line to a hostile in sight; a ring where a lost or called-out one was, with a line on to where a lost
    ///         one is guessed to be; a square on the locker a hidden one is in, faint if only suspected.
    /// </summary>
    private void DrawContact(DrawingHandleWorld worldHandle, SquadDebugContact contact, MapId mapId)
    {
        if (!TryGetPosition(contact.Member, mapId, out var memberPosition) ||
            !TryGetPosition(contact.Believed, mapId, out var believedPosition))
            return;

        switch (contact.State)
        {
            case NpcContactState.Visible:
                worldHandle.DrawLine(memberPosition, believedPosition, VisibleContactColor);
                break;

            case NpcContactState.Lost:
            case NpcContactState.Reported:
                var ringColor = contact.State == NpcContactState.Lost ? LostContactColor : ReportedContactColor;
                worldHandle.DrawCircle(believedPosition, ContactRadius, ringColor, false);

                if (contact.Predicted is { } predicted && TryGetPosition(predicted, mapId, out var predictedPosition))
                {
                    worldHandle.DrawLine(believedPosition, predictedPosition, ringColor);
                    worldHandle.DrawCircle(predictedPosition, ContactRadius * 0.4f, ringColor);
                }

                break;

            case NpcContactState.Concealed:
            case NpcContactState.Suspected:
                var hiddenColor = contact.State == NpcContactState.Concealed ? HiddenContactColor : HiddenContactColor.WithAlpha(0.4f);
                var halfSize = new Vector2(ContactRadius, ContactRadius);
                worldHandle.DrawRect(new Box2(believedPosition - halfSize, believedPosition + halfSize), hiddenColor, false);
                worldHandle.DrawLine(memberPosition, believedPosition, hiddenColor.WithAlpha(0.3f));
                break;
        }
    }

    private bool TryGetPosition(NetCoordinates netCoordinates, MapId mapId, out Vector2 position)
    {
        position = default;

        if (!_entityManager.TryGetEntity(netCoordinates.NetEntity, out _))
            return false;

        var mapCoordinates = _transformSystem.ToMapCoordinates(_entityManager.GetCoordinates(netCoordinates));
        if (mapCoordinates.MapId != mapId)
            return false;

        position = mapCoordinates.Position;
        return true;
    }

    /// <summary>
    ///     A stable, well-spread hue per squad: the golden ratio walks the colour wheel without repeating soon.
    /// </summary>
    private static Color GetSquadColor(NetEntity squad)
    {
        var hue = (squad.Id * 0.61803398875f) % 1f;
        return Color.FromHsv(new Vector4(hue, 0.85f, 1f, 1f));
    }
}
