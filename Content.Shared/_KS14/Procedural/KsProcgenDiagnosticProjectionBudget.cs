using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>One projection's metadata, geometry-collection and source-processing allowances.</summary>
internal sealed class KsProcgenDiagnosticProjectionBudget(int maximumEntries, int maximumCells, int maximumInputElements)
{
    internal int Entries { get; private set; }
    internal int Cells { get; private set; }
    internal int InputElements { get; private set; }
    internal bool EntriesTruncated { get; private set; }
    internal bool GeometryTruncated { get; private set; }
    private void Visit()
    {
        if (InputElements >= maximumInputElements) throw new KsProcgenDiagnosticProjectionLimitException();
        InputElements++;
    }
    internal IReadOnlyList<T> Read<T>(IEnumerable<T> source)
    {
        var result = new List<T>();
        foreach (var item in source) { Visit(); result.Add(item); }
        return KsProcgenReadOnly.Copy(result);
    }
    internal IReadOnlyList<T> Take<T>(IEnumerable<T> source) => Take(source, item => item);
    internal IReadOnlyList<TResult> Take<TSource, TResult>(IEnumerable<TSource> source, Func<TSource, TResult> project)
    {
        var result = new List<TResult>();
        foreach (var item in source)
        {
            Visit();
            if (Entries >= maximumEntries) { EntriesTruncated = true; break; }
            // Reserve the parent before nested entries; omitted records never consume geometry.
            Entries++;
            result.Add(project(item));
        }
        return KsProcgenReadOnly.Copy(result);
    }
    internal KsProcgenDiagnosticPath Geometry(IEnumerable<Vector2i> source, bool preserveOrder = false)
    {
        var input = Read(source);
        var ordered = preserveOrder ? input : KsProcgenGeometry.SortCells(input.Distinct());
        var count = Math.Min(ordered.Count, maximumCells - Cells);
        Cells += count;
        var truncated = count != ordered.Count;
        GeometryTruncated |= truncated;
        return new(ordered.Take(count).Select(cell => new KsProcgenDiagnosticCell(cell.X, cell.Y)).ToReadOnlyList(), truncated);
    }
}
internal sealed class KsProcgenDiagnosticProjectionLimitException : Exception;
