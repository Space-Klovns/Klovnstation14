using System.Linq;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenLayoutInstantiationSystem
{
    /// <summary>
    /// Opaque, system-bound, single-use materialization inputs. Mutable native loading nodes and
    /// geometric packing objects remain private until materialization has finished.
    /// </summary>
    internal sealed class PreparedLayout
    {
        private readonly KsProcgenLayoutInstantiationSystem _owner;
        private readonly KsProcgenPackingResult _packing;
        private readonly PreparedRoom[] _rooms;
        private readonly KsProcgenRoomEntityExtraction _extraction;
        private int _consumed;

        internal PreparedLayout(KsProcgenLayoutInstantiationSystem owner, string documentId, int revision,
            ulong projectDeclarationHash, string areaId, string sourceHash, KsProcgenRoomEntityExtraction extraction,
            KsProcgenPackingResult packing, PreparedRoom[] rooms, int entityCount, int terrainCells,
            int probes, int imageCharacters, KsProcgenNativeLayoutEligibility eligibility)
        {
            _owner = owner; _packing = packing; _rooms = rooms.ToArray(); _extraction = extraction;
            DocumentId = documentId; Revision = revision; ProjectDeclarationHash = projectDeclarationHash;
            AreaId = areaId; SourceContentHash = sourceHash; ExtractionContentHash = extraction.ContentHash;
            EntityCount = entityCount; TerrainCellCount = terrainCells; SourceTileProbes = probes;
            ImageCharacters = imageCharacters; Eligibility = eligibility;
            GeneratedCells = Array.AsReadOnly(packing.CellClaims
                .Where(entry => entry.Claim.Disposition == KsProcgenCellDisposition.ProceduralFloor)
                .Select(entry => entry.Cell).ToArray());
        }

        internal string DocumentId { get; }
        internal int Revision { get; }
        internal ulong ProjectDeclarationHash { get; }
        internal string AreaId { get; }
        internal string SourceContentHash { get; }
        internal string ExtractionContentHash { get; }
        internal int RoomCount => _rooms.Length;
        internal int EntityCount { get; }
        internal int TerrainCellCount { get; }
        internal int SourceTileProbes { get; }
        internal int ImageCharacters { get; }
        internal KsProcgenNativeLayoutEligibility Eligibility { get; }
        internal IReadOnlyList<Vector2i> GeneratedCells { get; }

        internal bool TryConsume(KsProcgenLayoutInstantiationSystem consumer) =>
            ReferenceEquals(_owner, consumer) && Interlocked.CompareExchange(ref _consumed, 1, 0) == 0;

        internal bool TryApply(KsProcgenLayoutInstantiationSystem consumer, EntityUid frameUid, List<EntityUid> allocations,
            int maximumAllocations, out KsProcgenInstantiatedLayout? layout, out KsProcgenIssue? issue,
            CancellationToken cancellationToken)
        {
            layout = null; issue = null;
            if (!ReferenceEquals(_owner, consumer))
                return Reject("PreparedLayoutOwnerMismatch", "Source layout plans belong to their originating system.", out issue);
            return consumer.Apply(this, _packing, _rooms, _extraction, frameUid, allocations, maximumAllocations,
                out layout, out issue, cancellationToken);
        }
    }
}
