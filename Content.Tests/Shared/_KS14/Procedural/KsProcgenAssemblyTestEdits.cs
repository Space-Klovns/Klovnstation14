using System;
using System.Linq;
using Content.Shared._KS14.Procedural;

namespace Content.Tests.Shared._KS14.Procedural;

/// <summary>Construct modified test inputs without mutating a retained compiled assembly.</summary>
internal static class KsProcgenAssemblyTestEdits
{
    internal static KsProcgenResolvedAssembly WithEntry(this KsProcgenResolvedAssembly assembly, string memberId,
        Func<KsProcgenResolvedEntityEntry, KsProcgenResolvedEntityEntry> edit) => assembly with
    {
        Members = assembly.Members.Select(member => member.Id == memberId ? member with { Entry = edit(member.Entry) } : member).ToArray(),
    };

    internal static KsProcgenResolvedAssembly WithEntries(this KsProcgenResolvedAssembly assembly,
        Func<KsProcgenResolvedEntityEntry, KsProcgenResolvedEntityEntry> edit) => assembly with
    {
        Members = assembly.Members.Select(member => member with { Entry = edit(member.Entry) }).ToArray(),
    };
}
