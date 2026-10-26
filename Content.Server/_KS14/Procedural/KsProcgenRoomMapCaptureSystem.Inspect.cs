using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenRoomMapCaptureSystem
{
    private bool TryInspect(string path, byte[] bytes, string hash, KsProcgenRoomMapCaptureLimits limits,
        CancellationToken cancellationToken, ref int nodes, ref int entities,
        DecodeWork decodeWork, out KsProcgenCapturedRoomMap? captured, out KsProcgenIssue? issue)
    {
        captured = null; issue = null;
        var text = new UTF8Encoding(false, true).GetString(bytes);
        // Preflight events before constructing recursive DataNodes. Aliases cannot expand a small
        // resource into an unbounded graph; deep inputs never enter the recursive engine parser.
        var parser = new Parser(new StringReader(text));
        var depth = 0; var documents = 0;
        while (parser.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = parser.Current;
            if (current is AnchorAlias || current is NodeEvent node && !node.Anchor.IsEmpty)
            { issue = new("UnsupportedRoomMapAlias", "Room-map capture does not accept YAML anchors or aliases."); return false; }
            if (current is DocumentStart && ++documents > 1)
            { issue = new("InvalidRoomMapDocument", "A room resource must contain exactly one YAML document."); return false; }
            if (current is NodeEvent && ++nodes > limits.MaximumYamlNodes)
            { issue = new("RoomMapYamlNodeBudget", "Cumulative YAML nodes exceed the capture allowance."); return false; }
            if (current is MappingStart or SequenceStart)
            {
                if (++depth > limits.MaximumYamlDepth)
                { issue = new("RoomMapYamlDepthBudget", "YAML nesting exceeds the capture allowance."); return false; }
            }
            else if (current is MappingEnd or SequenceEnd) depth--;
            if (current is Scalar scalar && scalar.Value.Length > limits.MaximumScalarCharacters)
            { issue = new("RoomMapScalarBudget", "A YAML scalar exceeds the capture allowance."); return false; }
        }
        if (documents != 1 || DataNodeParser.ParseYamlStream(new StringReader(text)).Single().Root is not MappingDataNode root ||
            !root.TryGet<MappingDataNode>("meta", out var meta) || !Integer(meta, "format", out var format) || format is not (6 or 7))
        { issue = new("UnsupportedRoomMapFormat", "Static room capture currently supports map formats 6 and 7."); return false; }
        if (!root.TryGet<MappingDataNode>("tilemap", out var tileMap) || !root.TryGet<SequenceDataNode>("entities", out var groups))
        { issue = new("InvalidRoomMapStructure", "Map resources require tile and entity tables."); return false; }
        var tileIds = new List<string>();
        var tileAliases = new Dictionary<int, string>();
        foreach (var (key, value) in tileMap.Children)
        {
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < 0 ||
                value is not ValueDataNode tile || !Id(tile.Value) || !tileAliases.TryAdd(index, tile.Value))
            { issue = new("InvalidRoomMapTile", "Map tile bindings require bounded numeric IDs and explicit prototype names."); return false; }
            tileIds.Add(tile.Value);
        }
        var entityIds = new List<string>();
        var components = new HashSet<string>(StringComparer.Ordinal);
        var uids = new HashSet<int>(); var fileEntities = 0;
        var grids = new List<KsProcgenCapturedMapGrid>();
        foreach (var groupNode in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (groupNode is not MappingDataNode group || !group.TryGet<ValueDataNode>("proto", out var proto) ||
                proto.Value != string.Empty && !Id(proto.Value) || !group.TryGet<SequenceDataNode>("entities", out var rows))
            { issue = new("InvalidRoomMapEntityGroup", "Each entity group requires a prototype reference and entity rows."); return false; }
            if (proto.Value != string.Empty) entityIds.Add(proto.Value);
            if (proto.Value != string.Empty && ProtoMan.TryIndex<EntityPrototype>(proto.Value, out var prototype) &&
                prototype.TryComp<MapGridComponent>(out _, Factory))
            { issue = new("UnsupportedInheritedRoomMapGrid", "Static grid decoding requires explicit source grid data, not inherited grid defaults."); return false; }
            foreach (var rowNode in rows)
            {
                if (++entities > limits.MaximumEntities)
                { issue = new("RoomMapEntityBudget", "Cumulative entity declarations exceed the capture allowance."); return false; }
                fileEntities++;
                if (rowNode is not MappingDataNode row || !Integer(row, "uid", out var uid) || uid < 1 || !uids.Add(uid))
                { issue = new("InvalidRoomMapEntityUid", "Map-local entity IDs must be positive and unique within the resource."); return false; }
                if (!row.TryGet<SequenceDataNode>("components", out var entries))
                {
                    if (!row.Has("components")) continue;
                    issue = new("InvalidRoomMapComponents", "Component overrides require a sequence."); return false;
                }
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in entries)
                {
                    if (entry is not MappingDataNode component || !component.TryGet<ValueDataNode>("type", out var type) ||
                        !Id(type.Value) || !seen.Add(type.Value) || !KnownSourceComponent(type.Value))
                    { issue = new("UnsupportedRoomMapComponent", "Component override names must be unique registered or intentionally ignored types."); return false; }
                    components.Add(type.Value);
                    if (type.Value == "MapGrid")
                    {
                        if (!TryDecodeGrid(uid, component, tileAliases, decodeWork, cancellationToken, out var grid, out issue)) return false;
                        grids.Add(grid!);
                    }
                }
            }
        }
        captured = new(path, bytes, hash, format, fileEntities, entityIds, tileIds, components, grids);
        return true;
    }

    private static bool Integer(MappingDataNode node, string key, out int value)
    {
        value = 0;
        return node.TryGet<ValueDataNode>(key, out var scalar) && int.TryParse(scalar.Value, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool Id(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;
    private bool KnownSourceComponent(string name) => Factory.TryGetRegistration(name, out _) || Factory.IsIgnored(name);
}
