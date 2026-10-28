using System.Linq;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Content.Server._KS14.Procedural;

public enum KsProcgenMapReferenceKind : byte { Entity, NetworkEntity, Map }
public sealed record KsProcgenMapEntityReference(string ComponentType, KsProcgenMapReferenceKind Kind, int TargetYamlUid);

/// <summary>Immutable, canonical map override data; references remain symbolic until instance remapping.</summary>
public sealed record KsProcgenMapComponentOverride(string ComponentType, string CanonicalBody);

public sealed record KsProcgenRoomEntityExtractionLimits
{
    public int MaximumMaskCellProbes { get; init; } = 524_288;
    public int MaximumGraphWork { get; init; } = 1_048_576;
    public int MaximumParentDepth { get; init; } = 128;
    public int MaximumExtractedEntities { get; init; } = 4096;
    public int MaximumOverrideNodes { get; init; } = 262_144;
    public int MaximumOverrideCharacters { get; init; } = 4_194_304;
    public int MaximumOverrideDepth { get; init; } = 64;
    public int MaximumDefaultImageCharacters { get; init; } = 4_194_304;
}

/// <summary>Detached source overrides, locators and transform witnesses, not a spawned or copy-authorized entity.</summary>
public sealed class KsProcgenExtractedMapEntity
{
    internal KsProcgenExtractedMapEntity(int yamlUid, string prototypeId, int parentYamlUid,
        Vector2 localPosition, double localRotation, Vector2 gridPosition, Vector2 roomPosition, double gridRotation,
        bool anchored, bool noLocalRotation, IEnumerable<string> overrides, IEnumerable<string> removed,
        IEnumerable<KsProcgenMapEntityReference> references, IEnumerable<string> uninspected,
        IEnumerable<KsProcgenMapComponentOverride> componentOverrides)
    {
        ComponentOverrides = Array.AsReadOnly(componentOverrides.OrderBy(component => component.ComponentType, StringComparer.Ordinal).ToArray());
        YamlUid = yamlUid; PrototypeId = prototypeId; ParentYamlUid = parentYamlUid;
        LocalPosition = localPosition; LocalRotationRadians = localRotation;
        GridPosition = gridPosition; RoomPosition = roomPosition; GridRotationRadians = gridRotation; Anchored = anchored; NoLocalRotation = noLocalRotation;
        OverrideComponentTypes = Array.AsReadOnly(overrides.OrderBy(name => name, StringComparer.Ordinal).ToArray());
        RemovedComponentTypes = Array.AsReadOnly(removed.OrderBy(name => name, StringComparer.Ordinal).ToArray());
        References = Array.AsReadOnly(references.Distinct().OrderBy(reference => reference.ComponentType, StringComparer.Ordinal)
            .ThenBy(reference => reference.Kind).ThenBy(reference => reference.TargetYamlUid).ToArray());
        UninspectedComponentTypes = Array.AsReadOnly(uninspected.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }
    public int YamlUid { get; }
    public string PrototypeId { get; }
    public int ParentYamlUid { get; }
    public Vector2 LocalPosition { get; }
    public double LocalRotationRadians { get; }
    public Vector2 GridPosition { get; }
    /// <summary>Composed grid position minus the room's source origin, before placement/rotation.</summary>
    public Vector2 RoomPosition { get; }
    public double GridRotationRadians { get; }
    public bool Anchored { get; }
    public bool NoLocalRotation { get; }
    /// <summary>Complete source override nodes, including type tags, isolated from mutable parser nodes.</summary>
    public IReadOnlyList<KsProcgenMapComponentOverride> ComponentOverrides { get; }
    public IReadOnlyList<string> OverrideComponentTypes { get; }
    public IReadOnlyList<string> RemovedComponentTypes { get; }
    /// <summary>Typed references observed by registered component validators, not a complete reference schema.</summary>
    public IReadOnlyList<KsProcgenMapEntityReference> References { get; }
    /// <summary>Known skipped/invalid overrides. An empty list does not prove a custom validator exposes every reference.</summary>
    public IReadOnlyList<string> UninspectedComponentTypes { get; }
}

public sealed class KsProcgenExtractedRoomEntities
{
    internal KsProcgenExtractedRoomEntities(KsProcgenRoomMapNativeMask mask, IEnumerable<KsProcgenExtractedMapEntity> entities)
    { Room = mask; Entities = Array.AsReadOnly(entities.ToArray()); }
    public KsProcgenRoomMapNativeMask Room { get; }
    /// <summary>Parents precede their children; IDs remain scoped to this room and source resource.</summary>
    public IReadOnlyList<KsProcgenExtractedMapEntity> Entities { get; }
}

public sealed class KsProcgenRoomEntityExtraction
{
    internal KsProcgenRoomEntityExtraction(string sourceHash, KsProcgenPrototypeManifest definitions,
        IEnumerable<KsProcgenExtractedRoomEntities> rooms, int probes, int graphWork,
        int defaultPrototypeImages = 0, int defaultImageCharacters = 0, int overrideNodes = 0, int overrideCharacters = 0)
    {
        SourceContentHash = sourceHash; Definitions = definitions; Rooms = Array.AsReadOnly(rooms.ToArray()); MaskCellProbes = probes; GraphWork = graphWork;
        OverrideNodes = overrideNodes; OverrideCharacters = overrideCharacters;
        DefaultPrototypeImages = defaultPrototypeImages; DefaultImageCharacters = defaultImageCharacters;
        ContentHash = ComputeContentHash();
    }
    public int SchemaVersion => 3;
    public string SourceContentHash { get; }
    public string ContentHash { get; }
    /// <summary>Includes typed entity prototypes discovered in overrides, captured before any materialization.</summary>
    public KsProcgenPrototypeManifest Definitions { get; }
    public IReadOnlyList<KsProcgenExtractedRoomEntities> Rooms { get; }
    public int MaskCellProbes { get; }
    public int GraphWork { get; }
    public int OverrideNodes { get; }
    public int OverrideCharacters { get; }
    public int DefaultPrototypeImages { get; }
    /// <summary>Canonical body and metadata input characters, charged once per reconstructed source prototype.</summary>
    public int DefaultImageCharacters { get; }
    public bool ComponentReferencesVerified => false;
    public bool CopyReady => false;

    private string ComputeContentHash()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var encoding = new UTF8Encoding(false, true);
        void Add(string value)
        {
            var bytes = encoding.GetBytes(value);
            hash.AppendData(Encoding.ASCII.GetBytes(bytes.Length.ToString(CultureInfo.InvariantCulture) + ":"));
            hash.AppendData(bytes);
        }
        void Number(int value) => Add(value.ToString(CultureInfo.InvariantCulture));
        void Real(double value) => Add(value.ToString("R", CultureInfo.InvariantCulture));
        void Point(Vector2 point)
        { Add(point.X.ToString("R", CultureInfo.InvariantCulture)); Add(point.Y.ToString("R", CultureInfo.InvariantCulture)); }
        void Strings(IReadOnlyList<string> values)
        { Number(values.Count); foreach (var value in values) Add(value); }
        Add("ks-room-entity-extraction-v3"); Add(SourceContentHash); Add(Definitions.ContentHash); Number(Rooms.Count);
        foreach (var room in Rooms)
        {
            var mask = room.Room;
            Add(mask.RoomAssetId); Add(mask.ResourcePath); Number(mask.GridYamlUid);
            Number(mask.SourceOrigin.X); Number(mask.SourceOrigin.Y); Number(mask.Cells.Count);
            foreach (var cell in mask.Cells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
            { Number(cell.X); Number(cell.Y); }
            Number(room.Entities.Count);
            foreach (var entity in room.Entities)
            {
                Number(entity.YamlUid); Add(entity.PrototypeId); Number(entity.ParentYamlUid);
                Point(entity.LocalPosition); Real(entity.LocalRotationRadians); Point(entity.GridPosition);
                Point(entity.RoomPosition); Real(entity.GridRotationRadians);
                Add(entity.Anchored ? "1" : "0"); Add(entity.NoLocalRotation ? "1" : "0");
                Number(entity.ComponentOverrides.Count);
                foreach (var component in entity.ComponentOverrides)
                { Add(component.ComponentType); Add(component.CanonicalBody); }
                Strings(entity.OverrideComponentTypes); Strings(entity.RemovedComponentTypes); Strings(entity.UninspectedComponentTypes);
                Number(entity.References.Count);
                foreach (var reference in entity.References)
                { Add(reference.ComponentType); Number((int) reference.Kind); Number(reference.TargetYamlUid); }
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
