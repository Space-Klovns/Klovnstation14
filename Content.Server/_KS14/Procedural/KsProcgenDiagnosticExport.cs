using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using Content.Shared._KS14.Procedural;

namespace Content.Server._KS14.Procedural;

public enum KsProcgenDiagnosticExportStatus : byte { Complete, InvalidInput, BudgetExceeded, Mismatch }
public sealed record KsProcgenDiagnosticExportResult(KsProcgenDiagnosticExportStatus Status,
    KsProcgenIssue? Issue, byte[] Data);

/// <summary>Canonical diagnostic JSON only; importing it never authorizes staging or publication.</summary>
public static class KsProcgenDiagnosticExport
{
    private static readonly JsonSerializerOptions Options = new()
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
            Converters = { new JsonStringEnumConverter() } };

    public static KsProcgenDiagnosticExportResult Serialize(KsProcgenOwnedDiagnosticReport report, int maximumBytes = 1_048_576)
    {
        if (report == null || maximumBytes is < 1 or > 16_777_216)
            return Fail(KsProcgenDiagnosticExportStatus.InvalidInput, "InvalidDiagnosticExportInput");
        using var stream = new KsProcgenBoundedDiagnosticStream(maximumBytes);
        try
        {
            JsonSerializer.Serialize(stream, report, options: Options);
            return new(KsProcgenDiagnosticExportStatus.Complete, null, stream.ToArray());
        }
        catch (KsProcgenDiagnosticSizeException)
        {
            return Fail(KsProcgenDiagnosticExportStatus.BudgetExceeded, "DiagnosticExportByteBudget");
        }
    }

    /// <summary>Authoring snapshot only; no inspected live access context or generated plan is implied.</summary>
    public static KsProcgenDiagnosticExportResult SerializeArea(KsProcgenAreaNormalization snapshot, int maximumBytes = 1_048_576)
    {
        if (snapshot == null || maximumBytes is < 1 or > 16_777_216 || snapshot.Blobs.Count > 4096 ||
            snapshot.Blobs.Sum(blob => (long) blob.Cells.Count) > 65_536)
            return Fail(KsProcgenDiagnosticExportStatus.InvalidInput, "InvalidAreaDiagnosticInput");
        using var stream = new KsProcgenBoundedDiagnosticStream(maximumBytes);
        try
        {
            var data = new
            {
                SchemaVersion = 3, Kind = "AreaSnapshot", Status = snapshot.Status.ToString(), ReasonCode = snapshot.Issue?.Code,
                SnapshotHash = snapshot.SnapshotHash.ToString(CultureInfo.InvariantCulture), snapshot.MarkerCount,
                Published = false, PlanGenerated = false, EngineContextVerified = false,
                Blobs = snapshot.Blobs.OrderBy(blob => blob.Id, StringComparer.Ordinal).Select(blob => new
                {
                    blob.Id, blob.Channel, ProfileId = blob.Profile.Id, blob.Profile.Theme,
                    Mode = blob.Profile.Mode.ToString(), GeometryMode = blob.Profile.GeometryMode.ToString(),
                    ConnectivityPolicy = blob.Profile.ConnectivityPolicy.ToString(), blob.Profile.RoomSubdivision,
                    blob.Profile.GenerationGoals,
                    blob.Profile.Atmosphere,
                    blob.Profile.Electrical,
                    Cells = blob.Cells.Select(cell => new KsProcgenDiagnosticCell(cell.X, cell.Y)),
                    Entrances = blob.Entrances.OrderBy(entrance => entrance.PortId, StringComparer.Ordinal).Select(entrance => new
                    {
                        entrance.PortId, entrance.OptionalSealable,
                        Thresholds = entrance.ThresholdCells.Select(cell => new KsProcgenDiagnosticCell(cell.X, cell.Y)),
                        InsideApproach = entrance.InsideApproach.Select(cell => new KsProcgenDiagnosticCell(cell.X, cell.Y)),
                        OutsideApproach = entrance.OutsideApproach.Select(cell => new KsProcgenDiagnosticCell(cell.X, cell.Y)),
                        InwardNormal = new KsProcgenDiagnosticCell(entrance.InwardNormal.X, entrance.InwardNormal.Y),
                    }),
                    Configurations = blob.ConnectionContract?.Configurations.Select(configuration => new
                    {
                        configuration.Id, configuration.Weight, Mode = configuration.Mode.ToString(),
                        IsolationScope = configuration.IsolationScope.ToString(), configuration.SealedPorts,
                        Groups = configuration.Groups.Select(group => new { group.Id, group.Ports,
                            RootCells = group.RootCells.Select(cell => new KsProcgenDiagnosticCell(cell.X, cell.Y)) }),
                    }),
                }),
            };
            JsonSerializer.Serialize(stream, data, options: Options);
            return new(KsProcgenDiagnosticExportStatus.Complete, null, stream.ToArray());
        }
        catch (KsProcgenDiagnosticSizeException)
        {
            return Fail(KsProcgenDiagnosticExportStatus.BudgetExceeded, "DiagnosticExportByteBudget");
        }
    }

    /// <summary>Compare canonical bytes to a fresh report. This checks exact diagnostics, not full content or engine replay.</summary>
    public static KsProcgenDiagnosticExportResult VerifyExact(byte[] supplied, KsProcgenOwnedDiagnosticReport fresh,
        int maximumBytes = 1_048_576)
    {
        if (supplied == null || maximumBytes is < 1 or > 16_777_216)
            return Fail(KsProcgenDiagnosticExportStatus.InvalidInput, "InvalidDiagnosticReplayInput");
        if (supplied.Length > maximumBytes)
            return Fail(KsProcgenDiagnosticExportStatus.BudgetExceeded, "DiagnosticExportByteBudget");
        var expected = Serialize(fresh, maximumBytes: maximumBytes);
        if (expected.Status != KsProcgenDiagnosticExportStatus.Complete) return expected;
        return supplied.SequenceEqual(expected.Data) ? new(KsProcgenDiagnosticExportStatus.Complete, null, []) :
            Fail(KsProcgenDiagnosticExportStatus.Mismatch, "DiagnosticReplayMismatch");
    }

    internal static KsProcgenDiagnosticExportResult Fail(KsProcgenDiagnosticExportStatus status, string code) =>
        new(status, new(code, "Diagnostic artifact did not complete."), []);

    internal sealed class KsProcgenDiagnosticSizeException : Exception { }

    internal sealed class KsProcgenBoundedDiagnosticStream(int maximumBytes) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Length + count > maximumBytes) throw new KsProcgenDiagnosticSizeException();
            base.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Length + buffer.Length > maximumBytes) throw new KsProcgenDiagnosticSizeException();
            base.Write(buffer);
        }
    }
}
