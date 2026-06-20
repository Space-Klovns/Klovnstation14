using System.Text.RegularExpressions;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.WordFilter;

/// <summary>
///     Basic regex for filtering words n shieet.
/// </summary>
[Prototype]
public sealed partial class WordFilterPrototype : IPrototype, ISerializationHooks
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public Regex Matcher = default!;

    [DataField(required: true)]
    public string Replacement = default!;

    [DataField(required: true)]
    public WordFilterCategory Category;
}

// bruh
public enum WordFilterCategory : byte
{
    Normal,
    Slur
}
