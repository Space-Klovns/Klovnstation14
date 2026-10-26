using System.Globalization;
using System.Linq;
using System.Xml;
using Content.Shared._KS14.Procedural;

namespace Content.Server._KS14.Procedural;

public static partial class KsProcgenDiagnosticSvg
{
    private static KsProcgenDiagnosticExportResult? ValidateSupported(KsProcgenOwnedDiagnosticReport report, long maximumCells)
    {
        if (report.SupportedEntities.Count > 4096 || report.OperatingAccess.Count > 4096 || report.Relations.Count > 4096 ||
            report.SupportedEntities.Sum(member => (long) member.Footprint.Cells.Count) +
            report.OperatingAccess.Sum(access => (long) access.CleanPath.Cells.Count) +
            report.Relations.Sum(relation => (long) relation.CleanPath.Cells.Count) > maximumCells)
            return KsProcgenDiagnosticExport.Fail(KsProcgenDiagnosticExportStatus.BudgetExceeded, "DiagnosticSvgSupportedBudget");
        bool Point(KsProcgenDiagnosticPoint point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && Math.Abs(point.X) <= 1_000_002f && Math.Abs(point.Y) <= 1_000_002f;
        bool Cells(KsProcgenDiagnosticPath path) => path.Cells.All(cell => Math.Abs((long) cell.X) <= 1_000_000 && Math.Abs((long) cell.Y) <= 1_000_000);
        bool Path(KsProcgenDiagnosticPath path) => Cells(path) && path.Cells.Skip(1).Select((cell, index) =>
            Math.Abs((long) cell.X - path.Cells[index].X) + Math.Abs((long) cell.Y - path.Cells[index].Y) == 1).All(cardinal => cardinal);
        if (report.SupportedEntities.Any(member => member.QuarterTurns is < 0 or > 3 || member.LocalQuarterTurns is < 0 or > 3 || !Point(member.LocalPosition) ||
                member.Layer is not ("Floor" or "Wall" or "Surface" or "Container") || !Cells(member.Footprint) ||
                (member.Exposed ? member.Position == null || !Point(member.Position) || member.Layer == "Container" : member.Position != null || member.Footprint.Cells.Count > 0)) ||
            report.OperatingAccess.Any(access => access.QuarterTurns is < 0 or > 3 || Math.Abs((long) access.Approach.X) > 1_000_000 ||
                Math.Abs((long) access.Approach.Y) > 1_000_000 || !Path(access.CleanPath)) ||
            report.Relations.Any(relation => !Path(relation.CleanPath)))
            return KsProcgenDiagnosticExport.Fail(KsProcgenDiagnosticExportStatus.InvalidInput, "InvalidDiagnosticSvgSupportedGeometry");
        return null;
    }

    private static void WriteSupported(XmlWriter writer, KsProcgenOwnedDiagnosticReport report)
    {
        void Number(string name, double value) => writer.WriteAttributeString(name, value.ToString(CultureInfo.InvariantCulture));
        void Title(string value) { writer.WriteStartElement("title"); writer.WriteString(value); writer.WriteEndElement(); }
        void Path(KsProcgenDiagnosticPath path, string style, string color, string title)
        {
            if (path.Cells.Count == 0) return;
            writer.WriteStartElement("polyline"); writer.WriteAttributeString("class", style);
            writer.WriteAttributeString("fill", "none"); writer.WriteAttributeString("stroke", color); writer.WriteAttributeString("stroke-width", "0.065");
            if (path.Truncated) writer.WriteAttributeString("stroke-dasharray", "0.14 0.1");
            writer.WriteAttributeString("points", string.Join(" ", path.Cells.Select(cell => FormattableString.Invariant($"{(double) cell.X + 0.5},{-(double) cell.Y - 0.5}"))));
            Title(title + (path.Truncated ? "; TRUNCATED PATH, completion unverified" : "")); writer.WriteEndElement();
        }
        foreach (var relation in report.Relations)
            Path(relation.CleanPath, "relation-path", relation.Severity == "Required" ? "#7b3294" : "#bf812d",
                $"{relation.Kind}: {relation.Subject.SourceMemberId} -> {relation.Target?.SourceMemberId}; {relation.Severity}/{relation.State}; distance {relation.PathDistance}");
        foreach (var access in report.OperatingAccess)
            Path(access.CleanPath, "operating-path", "#178b58", $"Operating access: {access.Member.SourceMemberId}; seat {access.AssociatedSeatMemberId ?? "none"}");
        foreach (var member in report.SupportedEntities.OrderBy(member => member.Layer == "Surface" ? 2 : member.Layer == "Wall" ? 1 : member.Exposed ? 0 : 3)
                     .ThenBy(member => member.RegionId, StringComparer.Ordinal).ThenBy(member => member.Member.MemberId, StringComparer.Ordinal))
        {
            writer.WriteStartElement("g"); writer.WriteAttributeString("class", member.Exposed ? "supported-member" : "contained-member");
            writer.WriteAttributeString("data-region", member.RegionId); writer.WriteAttributeString("data-member", member.Member.MemberId);
            writer.WriteAttributeString("data-layer", member.Layer);
            if (member.SupportParentMemberId != null) writer.WriteAttributeString("data-parent", member.SupportParentMemberId);
            Title($"{member.EntityId}; {member.Layer}; {member.Movement}; {member.Member.PackId}/{member.Member.CoreId}/{member.Member.CopyIndex}/{member.Member.SourceMemberId}; parent {member.SupportParentMemberId ?? "none"}; slot {member.SlotId ?? "none"}" +
                (member.Exposed ? "" : "; hidden container contents, no world footprint") + (member.Footprint.Truncated ? "; TRUNCATED FOOTPRINT" : ""));
            if (member.Exposed && member.Position is { } position)
            {
                var color = member.Layer == "Wall" ? "#1479ad" : member.Layer == "Surface" ? "#b67f0b" : "#a65f37";
                writer.WriteStartElement("circle"); Number("cx", (double) position.X); Number("cy", -(double) position.Y);
                Number("r", member.Layer == "Surface" ? 0.15 : 0.23); writer.WriteAttributeString("fill", color);
                writer.WriteAttributeString("stroke", "#101820"); writer.WriteAttributeString("stroke-width", "0.03"); writer.WriteEndElement();
                foreach (var cell in member.Footprint.Cells)
                {
                    writer.WriteStartElement("rect"); Number("x", (double) cell.X + 0.08); Number("y", -(double) cell.Y - 0.92);
                    Number("width", 0.84); Number("height", 0.84); writer.WriteAttributeString("fill", "none"); writer.WriteAttributeString("stroke", color);
                    writer.WriteAttributeString("stroke-width", "0.035"); writer.WriteEndElement();
                }
                var dx = member.QuarterTurns == 1 ? -0.35 : member.QuarterTurns == 3 ? 0.35 : 0.0;
                var dy = member.QuarterTurns == 0 ? 0.35 : member.QuarterTurns == 2 ? -0.35 : 0.0;
                writer.WriteStartElement("line"); writer.WriteAttributeString("class", "member-facing"); Number("x1", (double) position.X); Number("y1", -(double) position.Y);
                Number("x2", (double) position.X + dx); Number("y2", -(double) position.Y + dy);
                writer.WriteAttributeString("stroke", color); writer.WriteAttributeString("stroke-width", "0.06"); writer.WriteAttributeString("marker-end", "url(#facing)"); writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        foreach (var access in report.OperatingAccess)
        {
            writer.WriteStartElement("circle"); writer.WriteAttributeString("class", "operating-approach");
            Number("cx", (double) access.Approach.X + 0.5); Number("cy", -(double) access.Approach.Y - 0.5); Number("r", 0.31);
            writer.WriteAttributeString("fill", "none"); writer.WriteAttributeString("stroke", "#178b58"); writer.WriteAttributeString("stroke-width", "0.04");
            Title($"Reachable operating tile for {access.Member.SourceMemberId}; completion {(access.CleanPath.Truncated ? "unverified: truncated path" : "proposed only")}");
            writer.WriteEndElement();
        }
    }

    private static void WriteContainedSummary(XmlWriter writer, KsProcgenOwnedDiagnosticReport report, int minimumX, double firstY)
    {
        var hidden = report.SupportedEntities.Where(member => !member.Exposed).ToArray();
        if (hidden.Length == 0) return;
        void Line(string value, double y)
        {
            writer.WriteStartElement("text"); writer.WriteAttributeString("class", "contained-summary");
            writer.WriteAttributeString("x", minimumX.ToString(CultureInfo.InvariantCulture)); writer.WriteAttributeString("y", y.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("font-size", "0.35");
            writer.WriteStartElement("title"); writer.WriteString(value); writer.WriteEndElement();
            writer.WriteString(value.Length > 120 ? value[..120] + "..." : value); writer.WriteEndElement();
        }
        Line($"Hidden contents: {hidden.Length}; no world footprint. Full source identities are in JSON.", firstY);
        for (var index = 0; index < Math.Min(16, hidden.Length); index++)
        {
            var member = hidden[index];
            Line($"{member.EntityId} [{member.Member.CoreId}/{member.Member.CopyIndex}/{member.Member.SourceMemberId}] in {member.SupportParentMemberId}, slot {member.SlotId}", firstY + (double) (index + 1) * 0.5);
        }
    }
}
