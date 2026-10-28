using System.Globalization;
using System.Linq;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenRoomInstantiationSystem
{
    /// <summary>Internal single-use native loading inputs; never public mutable write authority.</summary>
    internal sealed class PreparedRoom(KsProcgenRoomInstantiationSystem owner, KsProcgenRoomEntityExtraction extraction,
        KsProcgenExtractedRoomEntities room, Vector2i origin, int quarterTurns,
        List<PreparedMember> members, int imageCharacters)
    {
        private int _consumed;
        internal KsProcgenRoomEntityExtraction Extraction { get; } = extraction;
        internal KsProcgenExtractedRoomEntities Room { get; } = room;
        internal Vector2i Origin { get; } = origin;
        internal int QuarterTurns { get; } = quarterTurns;
        internal IReadOnlyList<PreparedMember> Members { get; } = members.AsReadOnly();
        internal int ImageCharacters { get; } = imageCharacters;
        internal bool TryConsume(KsProcgenRoomInstantiationSystem consumer) =>
            ReferenceEquals(owner, consumer) && Interlocked.CompareExchange(ref _consumed, 1, 0) == 0;
    }

    /// <summary>Decode and freeze native load inputs before any terrain or entity writes.</summary>
    internal bool TryPrepare(KsProcgenRoomEntityExtraction extraction, string roomId, Vector2i origin, int quarterTurns,
        int maximumMembers, out PreparedRoom? plan, out KsProcgenIssue? issue,
        int maximumImageCharacters = 4_194_304, CancellationToken cancellationToken = default)
    {
        plan = null; issue = null;
        if (extraction == null || maximumMembers is < 0 or > KsProcgenPreviewOperationSystem.MaximumAllocationsPerStage ||
            maximumImageCharacters is < 0 or > 67_108_864 || quarterTurns is < 0 or > 3 ||
            origin.X is < -1_000_000 or > 1_000_000 || origin.Y is < -1_000_000 or > 1_000_000)
            return Reject("RoomInstantiationPreparedInput", "Room preparation needs bounded source, placement and work inputs.", out issue);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var room = extraction.Rooms.SingleOrDefault(room => room.Room.RoomAssetId == roomId);
            if (room == null) return Reject("RoomInstantiationMissingRoom", "No captured room has the requested identity.", out issue);
            if (room.Entities.Count > maximumMembers)
                return Reject("RoomInstantiationAllocationBudget", "Room identities exceed the remaining allocation allowance.", out issue);
            if (!_definitions.Matches(extraction.Definitions, out issue)) return false;
            long imageCharacters = 0;
            var prepared = new List<PreparedMember>();
            foreach (var entity in room.Entities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entity.UninspectedComponentTypes.Any(name => !Factory.IsIgnored(name)))
                    return Reject("RoomInstantiationUninspectedComponent", "A registered override could not be inspected before copying.", out issue);
                if (entity.OverrideComponentTypes.Any(name => name is "Map" or "MapGrid"))
                    return Reject("RoomInstantiationNestedFrame", "Room members cannot create nested map/grid frames.", out issue);
                EntityPrototype? prototype = null;
                if (entity.PrototypeId.Length != 0)
                {
                    var body = extraction.Definitions.Bodies.Single(body => body.Kind == KsProcgenPrototypeBodyKind.Entity && body.PrototypeId == entity.PrototypeId);
                    imageCharacters += (long) body.CanonicalBody.Length + body.CanonicalMetadata.Length;
                    if (imageCharacters > maximumImageCharacters) return ImageBudget(out issue);
                    if (!_definitions.TryCreatePrototypeSnapshot(extraction.Definitions, entity.PrototypeId, out prototype, out issue)) return false;
                }
                if (prototype != null && (prototype.Components.ContainsKey("Map") || prototype.Components.ContainsKey("MapGrid")))
                    return Reject("RoomInstantiationNestedFrame", "Room prototypes cannot create nested map/grid frames.", out issue);
                var defaults = new Dictionary<string, MappingDataNode>(StringComparer.Ordinal);
                if (prototype != null)
                    foreach (var (name, entry) in prototype.Components)
                        defaults.Add(name, _definitions.WriteCapturedComponent(entry.Component));
                var overrides = new Dictionary<string, MappingDataNode>(StringComparer.Ordinal);
                foreach (var image in entity.ComponentOverrides)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    imageCharacters += image.CanonicalBody.Length;
                    if (imageCharacters > maximumImageCharacters) return ImageBudget(out issue);
                    var limits = extraction.Definitions.Limits with { MaximumNodes = 1_048_576, MaximumCharacters = 16_777_216, MaximumDepth = 128 };
                    if (!KsProcgenPrototypeManifestSystem.TryDecodeCanonicalBody(image.CanonicalBody, limits, out var node, out _) ||
                        node is not MappingDataNode component || component.Get<ValueDataNode>("type").Value != image.ComponentType ||
                        !overrides.TryAdd(image.ComponentType, component))
                        return Reject("RoomInstantiationOverrideImage", "Captured override identities must match complete canonical mappings.", out issue);
                    component.Remove("type");
                }
                if (!entity.OverrideComponentTypes.SequenceEqual(overrides.Keys.OrderBy(name => name, StringComparer.Ordinal)))
                    return Reject("RoomInstantiationOverrideImage", "Every located override needs its captured component body.", out issue);
                if (!overrides.TryGetValue("Transform", out var transform)) overrides.Add("Transform", transform = new());
                if (!TryPose(entity, room.Room.GridYamlUid, origin, quarterTurns,
                        out var position, out var rotation, out issue)) return false;
                transform["parent"] = new ValueDataNode(entity.ParentYamlUid.ToString(CultureInfo.InvariantCulture));
                transform["pos"] = new ValueDataNode(position.X.ToString("R", CultureInfo.InvariantCulture) + "," + position.Y.ToString("R", CultureInfo.InvariantCulture));
                transform["rot"] = new ValueDataNode(rotation.ToString("R", CultureInfo.InvariantCulture) + " rad");
                transform["anchored"] = new ValueDataNode(entity.Anchored ? "true" : "false");
                transform["noRot"] = new ValueDataNode(entity.NoLocalRotation ? "true" : "false");
                prepared.Add(new(entity, prototype, overrides, defaults, entity.RemovedComponentTypes.ToHashSet(StringComparer.Ordinal)));
            }
            if (!_definitions.Matches(extraction.Definitions, out issue)) return false;
            plan = new(this, extraction, room, origin, quarterTurns, prepared, (int) imageCharacters);
            return true;
        }
        catch (OperationCanceledException)
        { return Reject("RoomInstantiationCancelled", "Room preparation was cancelled before loading.", out issue); }
        catch (Exception exception)
        {
            Log.Debug($"Captured room preparation failed: {exception}");
            return Reject("RoomInstantiationEngineFailure", "Captured component preparation failed before loading.", out issue);
        }
    }
}
