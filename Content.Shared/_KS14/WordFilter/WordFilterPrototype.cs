using System.Text.RegularExpressions;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.WordFilter;

/// <summary>
///     Basic regex for filtering words n shieet.
/// </summary>
[Prototype]
public sealed partial class WordFilterPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public Regex Matcher = default!;

    /// <summary>
    ///     If null, the replacement is an empty string.
    /// </summary>
    [DataField]
    public string? Replacement = null;

    [DataField(required: true)]
    public WordFilterCategory Category;
}

// bruh
public enum WordFilterCategory : byte
{
    Normal,
    Slur,

    /// <summary>
    ///     Special case that completely blocks the message from being sent if any of these
    ///         are matched.
    /// </summary>
    Prohibited
}
