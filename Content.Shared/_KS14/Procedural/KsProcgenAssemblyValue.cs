using System.Linq;

namespace Content.Shared._KS14.Procedural;

/// <summary>Detached selected-variant provenance and structural equality, including collection values.</summary>
internal static class KsProcgenAssemblyValue
{
    internal static KsProcgenResolvedAssembly Copy(KsProcgenResolvedAssembly assembly) => assembly with
    {
        Members = assembly.Members.Select(member => member with { Entry = member.Entry with
        {
            Footprint = KsProcgenReadOnly.Copy(member.Entry.Footprint),
            AllowedQuarterTurns = KsProcgenReadOnly.Copy(member.Entry.AllowedQuarterTurns),
        } }).ToReadOnlyList(),
        Relations = KsProcgenReadOnly.Copy(assembly.Relations),
    };

    internal static bool Same(KsProcgenResolvedAssembly? first, KsProcgenResolvedAssembly? second)
    {
        if (first?.Members == null || first.Relations == null || second?.Members == null || second.Relations == null ||
            first.Members.Count != second.Members.Count || first.Relations.Count != second.Relations.Count ||
            (first with { Members = second.Members, Relations = second.Relations }) != second ||
            first.Members.Any(member => member == null) || second.Members.Any(member => member == null) ||
            first.Relations.Any(relation => relation == null) || second.Relations.Any(relation => relation == null))
            return false;
        var ordered = second.Members.OrderBy(member => member.Id, StringComparer.Ordinal).ToArray();
        return first.Relations.OrderBy(relation => relation.Id, StringComparer.Ordinal)
                   .SequenceEqual(second.Relations.OrderBy(relation => relation.Id, StringComparer.Ordinal)) &&
               first.Members.OrderBy(member => member.Id, StringComparer.Ordinal).Select((member, index) =>
               {
                   var other = ordered[index];
                   return member?.Entry?.Footprint != null && member.Entry.AllowedQuarterTurns != null &&
                          other?.Entry?.Footprint != null && other.Entry.AllowedQuarterTurns != null &&
                          (member with { Entry = other.Entry }) == other &&
                          (member.Entry with { Footprint = other.Entry.Footprint, AllowedQuarterTurns = other.Entry.AllowedQuarterTurns }) == other.Entry &&
                          member.Entry.Footprint.SequenceEqual(other.Entry.Footprint) &&
                          member.Entry.AllowedQuarterTurns.SequenceEqual(other.Entry.AllowedQuarterTurns);
               }).All(equal => equal);
    }
}
