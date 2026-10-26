using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;
using Content.Shared._KS14.Procedural;

namespace Content.Server._KS14.Procedural;

/// <summary>Static tile schematic, never an operational map preview or a staging receipt.</summary>
public static partial class KsProcgenDiagnosticSvg
{
    private static readonly string[] RoomColors = ["#d4e7fa", "#e0efd1", "#f5dfbe", "#e7d8f2", "#ccece6"];

    public static KsProcgenDiagnosticExportResult Render(KsProcgenOwnedDiagnosticReport report,
        int maximumBytes = 1_048_576, int maximumCells = 8192, int maximumDimension = 4096)
    {
        if (report == null || maximumBytes is < 1 or > 16_777_216 || maximumCells is < 1 or > 65_536 ||
            maximumDimension is < 1 or > 65_536)
            return KsProcgenDiagnosticExport.Fail(KsProcgenDiagnosticExportStatus.InvalidInput, "InvalidDiagnosticSvgInput");
        var layers = new[] { report.RequestedCells, report.FloorCells, report.WallCells, report.VoidCells,
            report.EntranceCells, report.WindowCells };
        if (layers.Any(layer => layer.Count > maximumCells))
            return KsProcgenDiagnosticExport.Fail(KsProcgenDiagnosticExportStatus.BudgetExceeded, "DiagnosticSvgCellBudget");
        if (report.Rooms.Count > 4096 || report.Entities.Count > 4096 || report.Doors.Count > 4096 ||
            report.ProtectedCells.Count > maximumCells || report.PreservedCells.Count > maximumCells ||
            report.Rooms.Sum(room => (long) room.Cells.Count) > maximumCells ||
            report.Entities.Sum(entity => (long) entity.Footprint.Count) > maximumCells * 16L)
            return KsProcgenDiagnosticExport.Fail(KsProcgenDiagnosticExportStatus.BudgetExceeded, "DiagnosticSvgDetailBudget");
        if (report.Entities.Any(entity => entity.QuarterTurns is < 0 or > 3))
            return KsProcgenDiagnosticExport.Fail(KsProcgenDiagnosticExportStatus.InvalidInput, "InvalidDiagnosticSvgFacing");
        var supportedIssue = ValidateSupported(report, maximumCells * 16L);
        if (supportedIssue != null) return supportedIssue;
        var cells = layers.SelectMany(layer => layer).Distinct().ToArray();
        if (cells.Length == 0 || cells.Any(cell => Math.Abs((long) cell.X) > 1_000_000 || Math.Abs((long) cell.Y) > 1_000_000))
            return KsProcgenDiagnosticExport.Fail(KsProcgenDiagnosticExportStatus.InvalidInput, "InvalidDiagnosticSvgGeometry");
        if (cells.Length > maximumCells)
            return KsProcgenDiagnosticExport.Fail(KsProcgenDiagnosticExportStatus.BudgetExceeded, "DiagnosticSvgCellBudget");
        var minimumX = cells.Min(cell => cell.X);
        var maximumY = cells.Max(cell => cell.Y);
        var width = cells.Max(cell => cell.X) - minimumX + 1;
        var height = maximumY - cells.Min(cell => cell.Y) + 1;
        if (width > maximumDimension || height > maximumDimension)
            return KsProcgenDiagnosticExport.Fail(KsProcgenDiagnosticExportStatus.BudgetExceeded, "DiagnosticSvgDimensionBudget");
        var displayWidth = Math.Max(width + 2, 30);
        var containedRows = Math.Min(report.SupportedEntities.Count(member => !member.Exposed), 16);
        var displayHeight = (double) height + 4.0 + (containedRows > 0 ? (double) containedRows * 0.5 + 0.8 : 0.0);
        using var stream = new KsProcgenDiagnosticExport.KsProcgenBoundedDiagnosticStream(maximumBytes);
        try
        {
            using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
                { Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), Indent = true, CloseOutput = false }))
            {
                writer.WriteStartElement("svg", "http://www.w3.org/2000/svg");
                writer.WriteAttributeString("viewBox", FormattableString.Invariant($"{minimumX - 1} {-maximumY - 3} {displayWidth} {displayHeight}"));
                writer.WriteAttributeString("width", (displayWidth * 24).ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("height", (displayHeight * 24.0).ToString(CultureInfo.InvariantCulture));
                writer.WriteStartElement("title");
                writer.WriteString($"{report.RequestId}: {report.Status}; proposed geometry, engine access/gas/lighting unverified");
                writer.WriteEndElement();
                writer.WriteStartElement("defs");
                writer.WriteStartElement("marker");
                writer.WriteAttributeString("id", "facing"); writer.WriteAttributeString("viewBox", "0 0 10 10");
                writer.WriteAttributeString("refX", "8"); writer.WriteAttributeString("refY", "5");
                writer.WriteAttributeString("markerWidth", "4"); writer.WriteAttributeString("markerHeight", "4");
                writer.WriteAttributeString("orient", "auto"); writer.WriteStartElement("path");
                writer.WriteAttributeString("d", "M 0 0 L 10 5 L 0 10 z"); writer.WriteAttributeString("fill", "#a65f37");
                writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
                void Rectangle(KsProcgenDiagnosticCell cell, string color)
                {
                    writer.WriteStartElement("rect");
                    writer.WriteAttributeString("x", cell.X.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("y", (-cell.Y - 1).ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("width", "1"); writer.WriteAttributeString("height", "1");
                    writer.WriteAttributeString("fill", color); writer.WriteAttributeString("stroke", "#8b95a0");
                    writer.WriteAttributeString("stroke-width", "0.025"); writer.WriteEndElement();
                }
                void Dot(KsProcgenDiagnosticCell cell, string color, string radius)
                {
                    writer.WriteStartElement("circle");
                    writer.WriteAttributeString("cx", ((double) cell.X + 0.5).ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("cy", (-(double) cell.Y - 0.5).ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("r", radius); writer.WriteAttributeString("fill", color); writer.WriteEndElement();
                }
                foreach (var cell in report.RequestedCells) Rectangle(cell, "#ebedf0");
                var roomByCell = report.Rooms.SelectMany((room, index) => room.Cells.Select(cell => (cell, index)))
                    .GroupBy(entry => entry.cell).ToDictionary(group => group.Key, group => group.First().index);
                foreach (var cell in report.FloorCells)
                    Rectangle(cell, roomByCell.TryGetValue(cell, out var roomIndex) ? RoomColors[roomIndex % RoomColors.Length] : "#d4e7fa");
                foreach (var cell in report.WallCells) Rectangle(cell, "#35404a");
                foreach (var cell in report.WindowCells) Rectangle(cell, "#71c8e0");
                foreach (var cell in report.VoidCells) Rectangle(cell, "#b8bbc0");
                foreach (var cell in report.ProtectedCells) Dot(cell, "#278e59", "0.08");
                foreach (var cell in report.PreservedCells) Dot(cell, "#7d3c98", "0.16");
                foreach (var cell in report.EntranceCells) Dot(cell, "#f08b27", "0.22");
                foreach (var door in report.Doors) Dot(door.Threshold, "#007dba", "0.2");
                var supportedRegions = report.SupportedEntities.Select(entity => entity.RegionId).ToHashSet(StringComparer.Ordinal);
                foreach (var entity in report.Entities.Where(entity => !supportedRegions.Contains(entity.RegionId)))
                {
                    writer.WriteStartElement("g");
                    writer.WriteStartElement("title");
                    writer.WriteString($"{entity.EntityId}; declared movement: {entity.Movement}; pack/core: {entity.PackId}/{entity.CoreId}");
                    writer.WriteEndElement();
                    foreach (var cell in entity.Footprint) Dot(cell, "#a65f37", "0.25");
                    if (entity.Approach != null)
                    {
                        Dot(entity.Approach, "#a65f37", "0.08");
                        var dx = entity.QuarterTurns == 1 ? -0.4 : entity.QuarterTurns == 3 ? 0.4 : 0.0;
                        var dy = entity.QuarterTurns == 0 ? 0.4 : entity.QuarterTurns == 2 ? -0.4 : 0.0;
                        writer.WriteStartElement("line");
                        writer.WriteAttributeString("x1", ((double) entity.Cell.X + 0.5).ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y1", (-(double) entity.Cell.Y - 0.5).ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("x2", ((double) entity.Cell.X + 0.5 + dx).ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y2", (-(double) entity.Cell.Y - 0.5 + dy).ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("stroke", "#a65f37"); writer.WriteAttributeString("stroke-width", "0.06");
                        writer.WriteAttributeString("marker-end", "url(#facing)"); writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
                WriteSupported(writer, report);
                writer.WriteStartElement("text");
                writer.WriteAttributeString("x", minimumX.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("y", (-maximumY - 2).ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("font-size", "0.45");
                var shortId = report.RequestId.Length <= 48 ? report.RequestId : report.RequestId[..48] + "...";
                writer.WriteString(FormattableString.Invariant($"{shortId} / seed {report.Seed} / {report.Status}"));
                writer.WriteEndElement();
                writer.WriteStartElement("text");
                writer.WriteAttributeString("x", minimumX.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("y", (height - maximumY + 0.6).ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("font-size", "0.35");
                writer.WriteString(report.SupportedEntities.Count > 0
                    ? "Brown: floor; blue: wall; gold: surface. Green: access; purple: required relation. Proposed geometry."
                    : "Blue: doors; orange: entrances; green: protected paths; purple: preserved. Engine verification pending.");
                if (report.EntriesTruncated || report.GeometryTruncated || report.InputWorkTruncated) writer.WriteString(" TRUNCATED DIAGNOSTICS.");
                writer.WriteEndElement();
                WriteContainedSummary(writer, report, minimumX, (double) height - (double) maximumY + 1.2);
                writer.WriteEndElement();
            }
            return new(KsProcgenDiagnosticExportStatus.Complete, null, stream.ToArray());
        }
        catch (KsProcgenDiagnosticExport.KsProcgenDiagnosticSizeException)
        {
            return KsProcgenDiagnosticExport.Fail(KsProcgenDiagnosticExportStatus.BudgetExceeded, "DiagnosticExportByteBudget");
        }
    }
}
