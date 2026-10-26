using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenRoomMapCaptureSystem
{
    private sealed record SourceEntity(int Uid, string PrototypeId, int Parent, Vector2 Position, double Rotation,
        bool Anchored, bool NoRotation, bool IsMap, IReadOnlyList<string> Overrides, IReadOnlyList<string> Removed,
        IReadOnlyList<KsProcgenMapEntityReference> References, IReadOnlyList<string> Prototypes, IReadOnlyList<string> Uninspected,
        IReadOnlyList<KsProcgenPrototypeReference> TypedPrototypes, IReadOnlyList<KsProcgenResourceReference> Resources);
    private sealed record SourcePose(SourceEntity Entity, int GridUid, Vector2 Position, double Rotation, int Depth);
    private readonly record struct SourceDefaults(Vector2 Position, double Rotation, bool Anchored, bool NoRotation, bool IsMap);
    private sealed class EntityWork(KsProcgenRoomEntityExtractionLimits limits)
    {
        public readonly KsProcgenRoomEntityExtractionLimits Limits = limits;
        public int Used;
        public int Output;
        public int DefaultImageCharacters;
        public int DefaultPrototypeImages;
        public readonly Dictionary<string, SourceDefaults> Defaults = new(StringComparer.Ordinal);
        public bool Spend() => Used++ < Limits.MaximumGraphWork;
    }

    public bool TryExtractRoomEntities(KsProcgenProject project, KsProcgenRoomMapManifest source,
        out KsProcgenRoomEntityExtraction? extraction, out KsProcgenIssue? issue,
        KsProcgenRoomEntityExtractionLimits? limits = null, CancellationToken cancellationToken = default)
    {
        extraction = null; issue = null; limits ??= new();
        if (project == null || source == null || limits.MaximumMaskCellProbes is < 0 or > 2_097_152 ||
            limits.MaximumGraphWork is < 0 or > 4_194_304 || limits.MaximumParentDepth is < 1 or > 512 ||
            limits.MaximumExtractedEntities is < 0 or > 65_536 || limits.MaximumDefaultImageCharacters is < 0 or > 16_777_216)
            return Reject("InvalidRoomEntityExtractionInput", "Extraction needs captured input and bounded work/depth/output limits.", out issue);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_definitions.Matches(source.Definitions, out issue)) return false;
            if (!TryReconcileRoomMasks(project, source, out var masks, out issue,
                    maximumCellProbes: limits.MaximumMaskCellProbes, cancellationToken: cancellationToken)) return false;
            var work = new EntityWork(limits);
            var poses = new Dictionary<string, IReadOnlyList<SourcePose>>(StringComparer.Ordinal);
            var sources = new Dictionary<string, Dictionary<int, SourceEntity>>(StringComparer.Ordinal);
            foreach (var map in source.Maps)
            {
                if (!TryReadSourceEntities(map, source.Definitions, work, cancellationToken, out var entities, out issue) ||
                    !TryResolveSourcePoses(map, entities!, work, cancellationToken, out var resolved, out issue)) return false;
                poses.Add(map.ResourcePath, resolved!);
                sources.Add(map.ResourcePath, entities!);
            }
            var prototypeIds = source.Definitions.Roots.Where(root => root.Kind == KsProcgenPrototypeBodyKind.Entity).Select(root => root.PrototypeId);
            var referenceIds = source.Definitions.EntityReferenceSeeds.Concat(sources.Values.SelectMany(entities => entities.Values).SelectMany(entity => entity.Prototypes));
            var tileIds = source.Definitions.Roots.Where(root => root.Kind == KsProcgenPrototypeBodyKind.Tile).Select(root => root.PrototypeId);
            var typedIds = source.Definitions.TypedReferenceSeeds.Concat(sources.Values.SelectMany(entities => entities.Values).SelectMany(entity => entity.TypedPrototypes));
            var resourceReferences = source.Definitions.ResourceReferenceSeeds.Concat(sources.Values.SelectMany(entities => entities.Values).SelectMany(entity => entity.Resources));
            if (!_definitions.TryCaptureWithReferences(prototypeIds, tileIds, referenceIds, out var definitions, out issue, limits: source.Definitions.Limits,
                    dependencyRoots: source.Definitions.DependencyRoots, typedReferenceSeeds: typedIds, resourceReferenceSeeds: resourceReferences)) return false;
            var rooms = new List<KsProcgenExtractedRoomEntities>();
            foreach (var mask in masks!.Rooms)
            {
                var selected = new Dictionary<int, bool>();
                foreach (var pose in poses[mask.ResourcePath])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!work.Spend()) return EntityBudget(out issue);
                    if (pose.GridUid != mask.GridYamlUid || pose.Entity.Uid == mask.GridYamlUid) continue;
                    var cell = new Vector2i((int) MathF.Floor(pose.Position.X), (int) MathF.Floor(pose.Position.Y));
                    var inside = mask.Cells.Contains(cell);
                    if (pose.Entity.Parent != mask.GridYamlUid &&
                        (!selected.TryGetValue(pose.Entity.Parent, out var parentInside) || parentInside != inside))
                        return Reject("RoomMapExtractionCutsHierarchy", "A room extraction must contain or exclude complete non-grid transform branches.", out issue);
                    selected.Add(pose.Entity.Uid, inside);
                }
                var members = new List<KsProcgenExtractedMapEntity>();
                foreach (var pose in poses[mask.ResourcePath])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!work.Spend()) return EntityBudget(out issue);
                    if (!selected.GetValueOrDefault(pose.Entity.Uid)) continue;
                    if (!CheckSelectedReferences(pose.Entity, mask.GridYamlUid, selected, sources[mask.ResourcePath], work, out issue)) return false;
                    if (work.Output++ >= limits.MaximumExtractedEntities)
                        return Reject("RoomEntityExtractionOutputBudget", "Cumulative selected members exceed the extraction allowance.", out issue);
                    var entity = pose.Entity;
                    members.Add(new(entity.Uid, entity.PrototypeId, entity.Parent, entity.Position, entity.Rotation,
                        pose.Position, pose.Position - new Vector2((float) mask.SourceOrigin.X, (float) mask.SourceOrigin.Y),
                        pose.Rotation, entity.Anchored, entity.NoRotation, entity.Overrides, entity.Removed, entity.References, entity.Uninspected));
                }
                rooms.Add(new(mask, members));
            }
            if (!_definitions.Matches(source.Definitions, out issue) || !_definitions.Matches(definitions!, out issue)) return false;
            extraction = new(source.ContentHash, definitions!, rooms, masks.CellProbes, work.Used,
                defaultPrototypeImages: work.DefaultPrototypeImages, defaultImageCharacters: work.DefaultImageCharacters); return true;
        }
        catch (OperationCanceledException)
        { return Reject("RoomEntityExtractionCancelled", "Source entity extraction was cancelled.", out issue); }
        catch (Exception exception)
        {
            Log.Debug($"Source entity extraction failed: {exception}");
            return Reject("UnsupportedRoomEntitySource", "Source entity data could not be decoded without instantiation.", out issue);
        }
    }

    private bool TryReadSourceEntities(KsProcgenCapturedRoomMap map, KsProcgenPrototypeManifest definitions,
        EntityWork work, CancellationToken cancellationToken,
        out Dictionary<int, SourceEntity>? entities, out KsProcgenIssue? issue)
    {
        entities = null; issue = null;
        using var stream = map.OpenRead();
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        var root = (MappingDataNode) DataNodeParser.ParseYamlStream(reader).Single().Root;
        var result = new Dictionary<int, SourceEntity>();
        foreach (var group in root.Get<SequenceDataNode>("entities").Cast<MappingDataNode>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!work.Spend()) return EntityBudget(out issue);
            var rows = group.Get<SequenceDataNode>("entities");
            if (rows.Count == 0) continue;
            var prototypeId = group.Get<ValueDataNode>("proto").Value;
            if (!TryReadSourceDefaults(prototypeId, definitions, work, cancellationToken, out var defaults, out issue)) return false;
            foreach (var row in rows.Cast<MappingDataNode>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!work.Spend()) return EntityBudget(out issue);
                var position = defaults.Position;
                var rotation = defaults.Rotation;
                var anchored = defaults.Anchored; var noRotation = defaults.NoRotation;
                var parent = 0; var overrides = new List<string>(); var removed = new List<string>();
                var references = new List<KsProcgenMapEntityReference>(); var prototypeIds = new List<string>();
                var typedPrototypes = new List<KsProcgenPrototypeReference>();
                var resources = new List<KsProcgenResourceReference>();
                var uninspected = new List<string>();
                if (row.TryGet<SequenceDataNode>("components", out var components))
                foreach (var component in components.Cast<MappingDataNode>())
                {
                    if (!work.Spend()) return EntityBudget(out issue);
                    var type = component.Get<ValueDataNode>("type").Value; overrides.Add(type);
                    if (type != "Transform") continue;
                    if (component.TryGet<ValueDataNode>("parent", out var parentNode) && parentNode.Value != "invalid" &&
                        (!Integer(component, "parent", out parent) || parent < 1))
                        return Reject("InvalidRoomEntityParent", "Transform parents must be invalid or positive map-local entity IDs.", out issue);
                    if (component.Has("parent") && parentNode == null)
                        return Reject("InvalidRoomEntityParent", "Transform parent references must be scalar values.", out issue);
                    if (component.Has("pos")) position = _serialization.Read<Vector2>(component["pos"]);
                    if (component.Has("rot")) rotation = _serialization.Read<Angle>(component["rot"]).Theta;
                    if (component.Has("anchored")) anchored = _serialization.Read<bool>(component["anchored"]);
                    if (component.Has("noRot")) noRotation = _serialization.Read<bool>(component["noRot"]);
                }
                if (row.Has("missingComponents"))
                {
                    var overridden = overrides.ToHashSet(StringComparer.Ordinal);
                    var removedNames = new HashSet<string>(StringComparer.Ordinal);
                    if (!row.TryGet<SequenceDataNode>("missingComponents", out var missing))
                        return Reject("InvalidRoomEntityComponentRemoval", "Removed component names require a sequence.", out issue);
                    foreach (var missingNode in missing)
                    {
                        if (!work.Spend()) return EntityBudget(out issue);
                        if (missingNode is not ValueDataNode name || !KnownSourceComponent(name.Value) || !removedNames.Add(name.Value) || overridden.Contains(name.Value))
                            return Reject("InvalidRoomEntityComponentRemoval", "Removed component types must be known, unique and not overridden.", out issue);
                        if (name.Value is "Transform" or "MetaData" or "MapGrid" or "Map")
                            return Reject("UnsupportedRoomEntityCoreRemoval", "Source extraction cannot remove transform/map/grid components.", out issue);
                        removed.Add(name.Value);
                    }
                }
                if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !double.IsFinite(rotation) || noRotation && rotation != 0.0)
                    return Reject("InvalidRoomEntityTransform", "Source poses must be finite and respect no-local-rotation constraints.", out issue);
                var uid = row.Get<ValueDataNode>("uid").AsInt();
                if (row.TryGet<SequenceDataNode>("components", out var componentNodes))
                foreach (var component in componentNodes.Cast<MappingDataNode>())
                    if (!TryInspectComponentReferences(uid, component, references, prototypeIds, typedPrototypes, resources, uninspected, work, cancellationToken, out issue)) return false;
                result.Add(uid, new(uid, prototypeId, parent, position, rotation, anchored, noRotation,
                    defaults.IsMap || overrides.Contains("Map"), overrides.AsReadOnly(), removed.AsReadOnly(),
                    references.AsReadOnly(), prototypeIds.AsReadOnly(), uninspected.AsReadOnly(), typedPrototypes.AsReadOnly(), resources.AsReadOnly()));
            }
        }
        entities = result; return true;
    }

    private static bool EntityBudget(out KsProcgenIssue? issue) =>
        Reject("RoomEntityExtractionGraphBudget", "Source entity traversal exhausted its shared work allowance.", out issue);
}
