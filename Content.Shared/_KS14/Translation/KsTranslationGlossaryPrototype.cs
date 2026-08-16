using System.Collections.Generic;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Translation;

/// <summary>
///     One directional term dictionary compiled into the KS14 DeepL chat-translation glossary. Every
///     prototype of this type contributes one (source -> target) dictionary to a single multilingual
///     glossary. A glossary forces exact substitutions, so it is mainly for setting jargon and proper nouns
///     that must NOT be translated literally (map a term to itself to keep it verbatim across languages).
///     <see cref="Source"/> and <see cref="Target"/> are BASE language codes (e.g. "EN", "DE"); a base target
///     covers its regional variants. Hand-editable; not all language pairs support DeepL glossaries, and an
///     unsupported pair makes the whole glossary fall back to none, so stick to well-supported pairs.
/// </summary>
[Prototype]
public sealed partial class KsTranslationGlossaryPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Base source language code, e.g. "EN".</summary>
    [DataField(required: true)]
    public string Source = default!;

    /// <summary>Base target language code, e.g. "DE".</summary>
    [DataField(required: true)]
    public string Target = default!;

    /// <summary>Exact term substitutions applied when translating this pair: source term -> target term.</summary>
    [DataField]
    public Dictionary<string, string> Entries = new();
}
