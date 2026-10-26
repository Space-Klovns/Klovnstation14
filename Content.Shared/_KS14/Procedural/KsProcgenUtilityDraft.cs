using System.Linq;

namespace Content.Shared._KS14.Procedural;

/// <summary>
/// Portable utility asset document. This subset does not contain room assignments, live UIDs,
/// permissions or verification receipts. Asset names are scoped by document and utility kind.
/// </summary>
[DataDefinition]
public sealed partial class KsProcgenUtilityDraftSpec
{
    [DataField] public int SchemaVersion = 1;
    [DataField(required: true)] public string DocumentId = string.Empty;
    [DataField] public int Revision;
    [DataField] public Dictionary<string, KsProcgenAtmosphereSpec> Atmosphere = new(StringComparer.Ordinal);
    [DataField] public Dictionary<string, KsProcgenElectricalSpec> Electrical = new(StringComparer.Ordinal);
}

public sealed record KsProcgenDraftAtmosphere(string Id, KsProcgenAtmosphereProfile Profile);
public sealed record KsProcgenDraftElectrical(string Id, KsProcgenElectricalProfile Profile);

/// <summary>
/// Validated immutable utility catalog for one document revision. Retain this instance for a
/// preview; editing its source or a returned editable copy cannot change that preview's assets.
/// Loaded prototype dependencies still require a separate content manifest before replay/apply.
/// </summary>
public sealed class KsProcgenUtilityDraft
{
    public const int MaximumAssets = 128;

    private KsProcgenUtilityDraft(string documentId, int revision,
        IEnumerable<KsProcgenDraftAtmosphere> atmosphere, IEnumerable<KsProcgenDraftElectrical> electrical)
    {
        DocumentId = documentId;
        Revision = revision;
        Atmosphere = KsProcgenReadOnly.Copy(atmosphere);
        Electrical = KsProcgenReadOnly.Copy(electrical);
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-utility-assets-v1");
        hash.AddInt(Atmosphere.Count);
        foreach (var asset in Atmosphere) asset.Profile.AddHash(ref hash);
        hash.AddInt(Electrical.Count);
        foreach (var asset in Electrical) asset.Profile.AddHash(ref hash);
        ContentHash = hash.Value;
    }

    public int SchemaVersion => 1;
    public string DocumentId { get; }
    public int Revision { get; }
    /// <summary>Canonical utility declarations only; excludes document identity and edit revision.</summary>
    public ulong ContentHash { get; }
    public IReadOnlyList<KsProcgenDraftAtmosphere> Atmosphere { get; }
    public IReadOnlyList<KsProcgenDraftElectrical> Electrical { get; }

    public KsProcgenUtilityDraftSpec CopySpec() => new()
    {
        DocumentId = DocumentId, Revision = Revision,
        Atmosphere = Atmosphere.ToDictionary(asset => asset.Id, asset => asset.Profile.CopySpec(), StringComparer.Ordinal),
        Electrical = Electrical.ToDictionary(asset => asset.Id, asset => asset.Profile.CopySpec(), StringComparer.Ordinal),
    };

    public bool TryAtmosphere(string id, out KsProcgenAtmosphereProfile? profile)
    {
        profile = Atmosphere.FirstOrDefault(asset => asset.Id == id)?.Profile;
        return profile != null;
    }

    public bool TryElectrical(string id, out KsProcgenElectricalProfile? profile)
    {
        profile = Electrical.FirstOrDefault(asset => asset.Id == id)?.Profile;
        return profile != null;
    }

    /// <summary>
    /// All-or-nothing import validation. Dependency callbacks must describe the receiving content
    /// catalog; accepting these declarations does not inspect capabilities or authorize writes.
    /// </summary>
    public static bool TrySnapshot(KsProcgenUtilityDraftSpec spec, Func<string, bool> gasExists,
        Func<string, bool> entityExists, out KsProcgenUtilityDraft? draft, out KsProcgenIssue? issue)
    {
        draft = null;
        issue = null;
        if (spec == null || spec.SchemaVersion != 1)
        {
            issue = new("UnsupportedUtilityDraftSchema", "Utility draft schema version 1 is required.");
            return false;
        }
        if (!KsProcgenAreaNormalizer.ValidId(spec.DocumentId) || spec.Revision < 0 ||
            spec.Atmosphere == null || spec.Electrical == null ||
            spec.Atmosphere.Count > MaximumAssets || spec.Electrical.Count > MaximumAssets - spec.Atmosphere.Count ||
            gasExists == null || entityExists == null)
        {
            issue = new("InvalidUtilityDraft", "A utility draft needs a bounded ID, nonnegative revision and bounded asset collections.");
            return false;
        }
        var atmosphere = new List<KsProcgenDraftAtmosphere>();
        var electrical = new List<KsProcgenDraftElectrical>();
        foreach (var (id, payload) in spec.Atmosphere.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!KsProcgenAtmosphereProfiles.TrySnapshot(payload, out var profile, out issue,
                    sourceProfileId: id, gasExists: gasExists)) return false;
            atmosphere.Add(new(id, profile!));
        }
        foreach (var (id, payload) in spec.Electrical.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!KsProcgenElectricalProfiles.TrySnapshot(payload, out var profile, out issue,
                    sourceProfileId: id, entityExists: entityExists)) return false;
            electrical.Add(new(id, profile!));
        }
        draft = new(spec.DocumentId, spec.Revision, atmosphere, electrical);
        return true;
    }

    /// <summary>
    /// Full-document revision check for headless authoring. The owner must serialize replacement
    /// of its current instance; this value object is not a network permission or writer lease.
    /// Payload carries the revision being edited. Successful replacement advances it once.
    /// </summary>
    public bool TryRevise(KsProcgenUtilityDraftSpec replacement, Func<string, bool> gasExists,
        Func<string, bool> entityExists, out KsProcgenUtilityDraft? revised, out KsProcgenIssue? issue)
    {
        revised = null;
        if (replacement == null || replacement.DocumentId != DocumentId || replacement.Revision != Revision || Revision == int.MaxValue)
        {
            issue = new("UtilityDraftRevisionConflict", "Replacement must target this document and its current, incrementable revision.");
            return false;
        }
        if (!TrySnapshot(replacement, gasExists, entityExists, out var validated, out issue)) return false;
        revised = new(DocumentId, Revision + 1, validated!.Atmosphere, validated.Electrical);
        return true;
    }
}
