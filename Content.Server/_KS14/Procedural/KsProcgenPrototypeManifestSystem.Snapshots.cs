using System.Linq;
using System.Text;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;

namespace Content.Server._KS14.Procedural;

internal sealed class KsProcgenCapturedPrototypeException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed partial class KsProcgenPrototypeManifestSystem
{
    internal bool TryCreatePrototypeSnapshot(KsProcgenPrototypeManifest manifest, string id,
        out EntityPrototype? snapshot, out KsProcgenIssue? issue)
    {
        snapshot = null; issue = null;
        var body = manifest.Bodies.FirstOrDefault(body => body.Kind == KsProcgenPrototypeBodyKind.Entity && body.PrototypeId == id);
        if (body == null)
        { issue = new("UncapturedPrototypeReference", "Captured allocation needs an entity body in the manifest."); return false; }
        try
        {
            if (!TryDecodeCanonicalBody(body.CanonicalBody, manifest.Limits, out var node, out var code) ||
                !TryDecodeCanonicalBody(body.CanonicalMetadata, manifest.Limits, out var metadataNode, out code))
            { issue = new(code!, "Captured prototype data is not a valid bounded canonical image."); return false; }
            if (node is not MappingDataNode || metadataNode is not MappingDataNode metadata ||
                !metadata.TryGet<SequenceDataNode>("categories", out var categories) || categories.Count > manifest.Limits.MaximumCategoriesPerEntity)
            { issue = new("UnsupportedCapturedPrototypeMetadata", "Captured metadata requires a bounded category registry."); return false; }
            var context = new PrototypeContext(_serializationManager);
            var decoded = _serializationManager.Read<EntityPrototype>(node, context: context, notNullableOverride: true);
            if (decoded.ID != id || decoded.Abstract)
            { issue = new("CapturedPrototypeIdentityMismatch", "Decoded prototype identity must match a concrete captured entity."); return false; }
            var hide = _serializationManager.Read<bool>(metadata["hideSpawnMenu"], context: context);
            var categoryData = categories.Select(category => _serializationManager.Read<EntityCategoryPrototype>(category,
                context: context, notNullableOverride: true)).ToArray();
            var candidate = EntityPrototype.KsFromCapturedData(decoded, hide, categoryData, _serializationManager, context);
            if (!MatchesSnapshot(manifest, candidate, out issue)) return false;
            snapshot = candidate; return true;
        }
        catch (Exception exception)
        {
            Log.Debug($"Captured prototype reconstruction failed: {exception}");
            issue = new("UnsupportedCapturedPrototype", "Captured data could not reconstruct an independent prototype image."); return false;
        }
    }

    internal bool MatchesSnapshot(KsProcgenPrototypeManifest manifest, EntityPrototype prototype, out KsProcgenIssue? issue)
    {
        issue = null;
        var expected = manifest.Bodies.FirstOrDefault(body => body.Kind == KsProcgenPrototypeBodyKind.Entity && body.PrototypeId == prototype.ID);
        if (expected == null)
        { issue = new("UncapturedPrototypeReference", "The instance metadata references an uncaptured prototype."); return false; }
        try
        {
            var context = new PrototypeContext(_serializationManager); var work = new CaptureWork(manifest.Limits);
            var node = _serializationManager.WriteValue(prototype, alwaysWrite: true, context: context, notNullableOverride: true);
            if (!TryMetadataNode(prototype, manifest.Limits, context, out var metadata, out issue)) return false;
            var canonical = new StringBuilder(); var canonicalMetadata = new StringBuilder();
            if (!work.Write(node, canonical, 0, normalizeEntity: true) || !work.Write(metadata!, canonicalMetadata, 0))
            { issue = new(work.IssueCode!, "Snapshot inspection exceeds the captured structural allowance."); return false; }
            if (canonical.ToString() == expected.CanonicalBody && canonicalMetadata.ToString() == expected.CanonicalMetadata) return true;
            issue = new("CapturedPrototypeRoundtripDrift", "Reconstructed or instance prototype data differs from its captured image."); return false;
        }
        catch (Exception exception)
        {
            Log.Debug($"Captured prototype inspection failed: {exception}");
            issue = new("UnsupportedCapturedPrototype", "Instance prototype defaults could not be inspected."); return false;
        }
    }

    internal EntityUid SpawnCaptured(KsProcgenPrototypeManifest manifest, string id, EntityCoordinates coordinates)
    {
        // Rebuild every placement from immutable strings, isolating later placements from mutable
        // fields that custom serializers or copy-by-reference attributes might otherwise share.
        if (!TryCreatePrototypeSnapshot(manifest, id, out var snapshot, out var issue))
            throw new KsProcgenCapturedPrototypeException(issue!.Code);
        var uid = EntityManager.KsCreateEntityUninitializedFromSnapshot(snapshot!, coordinates,
            new PrototypeContext(_serializationManager));
        EntityManager.InitializeAndStartEntity(uid);
        return uid;
    }
}
