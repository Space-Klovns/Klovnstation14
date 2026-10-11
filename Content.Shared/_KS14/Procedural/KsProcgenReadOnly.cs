using System.Collections.Immutable;

namespace Content.Shared._KS14.Procedural;

/// <summary>Detached immutable list snapshots using APIs permitted by the client sandbox.</summary>
internal static class KsProcgenReadOnly
{
    internal static IReadOnlyList<T> Copy<T>(IEnumerable<T> items) => items.ToImmutableArray();
    internal static IReadOnlyList<T> ToReadOnlyList<T>(this IEnumerable<T> items) => Copy(items);
}
