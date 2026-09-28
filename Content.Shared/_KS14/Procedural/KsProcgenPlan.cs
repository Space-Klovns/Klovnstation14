using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenCellDisposition : byte
{
    Preserved,
    Prefab,
    ProceduralFloor,
    Passage,
    Structure,
    SealedSolid,
}

public readonly record struct KsProcgenCellClaim(string OwnerId, KsProcgenCellDisposition Disposition);

/// <summary>
/// A speculative, exact-cell plan. A checkpoint captures every claim made by a candidate package.
/// </summary>
public sealed class KsProcgenPlan
{
    private readonly KsProcgenNormalizedShape _shape;
    private readonly Dictionary<Vector2i, KsProcgenCellClaim> _claims = new();
    private readonly List<Vector2i> _journal = new();

    public KsProcgenPlan(KsProcgenNormalizedShape shape)
    {
        _shape = shape;
        foreach (var cell in shape.PreservedCells)
            _claims.Add(cell, new KsProcgenCellClaim("<preserved>", KsProcgenCellDisposition.Preserved));
    }

    public int Checkpoint() => _journal.Count;

    public bool TryClaim(Vector2i cell, KsProcgenCellClaim claim)
    {
        if (string.IsNullOrWhiteSpace(claim.OwnerId) ||
            !Enum.IsDefined(claim.Disposition) ||
            claim.Disposition == KsProcgenCellDisposition.Preserved ||
            !_shape.CanWrite(cell) ||
            (_shape.ContainsEnvelope(cell) && claim.Disposition is not
                (KsProcgenCellDisposition.Structure or KsProcgenCellDisposition.Passage)) ||
            _claims.ContainsKey(cell))
            return false;

        _claims.Add(cell, claim);
        _journal.Add(cell);
        return true;
    }

    public void Rollback(int checkpoint)
    {
        if (checkpoint < 0 || checkpoint > _journal.Count)
            throw new ArgumentOutOfRangeException(nameof(checkpoint));

        for (var index = _journal.Count - 1; index >= checkpoint; index--)
            _claims.Remove(_journal[index]);

        _journal.RemoveRange(checkpoint, _journal.Count - checkpoint);
    }

    public bool TryGetClaim(Vector2i cell, out KsProcgenCellClaim claim) =>
        _claims.TryGetValue(cell, out claim);

    public IReadOnlyList<Vector2i> UnassignedTargetCells() =>
        _shape.TargetCells.Where(cell => !_claims.ContainsKey(cell)).ToArray();

    public bool IsTargetComplete() => UnassignedTargetCells().Count == 0;

    public IReadOnlyList<(Vector2i Cell, KsProcgenCellClaim Claim)> SnapshotTargetClaims()
    {
        var claims = new List<(Vector2i Cell, KsProcgenCellClaim Claim)>();
        foreach (var cell in _shape.TargetCells)
        {
            if (_claims.TryGetValue(cell, out var claim))
                claims.Add((cell, claim));
        }

        return claims;
    }

    public ulong SemanticHash(string requestId, int seed, int generatorVersion)
    {
        var hash = KsProcgenStableHash.Create();
        hash.AddString(requestId);
        hash.AddInt(seed);
        hash.AddInt(generatorVersion);

        foreach (var (cell, claim) in _claims.OrderBy(entry => entry.Key.Y).ThenBy(entry => entry.Key.X))
        {
            hash.AddInt(cell.X);
            hash.AddInt(cell.Y);
            hash.AddString(claim.OwnerId);
            hash.AddInt((int) claim.Disposition);
        }

        return hash.Value;
    }
}

/// <summary>
/// Explicit FNV-1a byte encoding. Never depends on runtime string or dictionary hash order.
/// </summary>
public struct KsProcgenStableHash
{
    private const ulong OffsetBasis = 14_695_981_039_346_656_037;
    private const ulong Prime = 1_099_511_628_211;

    public ulong Value { get; private set; }

    public static KsProcgenStableHash Create() => new() { Value = OffsetBasis };

    public void AddInt(int value)
    {
        var number = unchecked((uint) value);
        for (var index = 0; index < 4; index++)
        {
            AddByte((byte) number);
            number >>= 8;
        }
    }

    public void AddString(string value)
    {
        AddInt(value.Length);
        foreach (var character in value)
        {
            AddByte((byte) character);
            AddByte((byte) (character >> 8));
        }
    }

    private void AddByte(byte value)
    {
        Value = unchecked((Value ^ value) * Prime);
    }
}

/// <summary>
/// A pinned SplitMix64 stream. Stage and stable instance names derive independent streams.
/// </summary>
public sealed class KsProcgenRandom
{
    private ulong _state;

    public KsProcgenRandom(ulong seed)
    {
        _state = seed;
    }

    public static KsProcgenRandom ForStage(int rootSeed, string stageName, string instanceId)
    {
        var hash = KsProcgenStableHash.Create();
        hash.AddInt(rootSeed);
        hash.AddString(stageName);
        hash.AddString(instanceId);
        return new KsProcgenRandom(hash.Value);
    }

    public ulong NextUInt64()
    {
        _state = unchecked(_state + 0x9E3779B97F4A7C15UL);
        var value = _state;
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
        return value ^ (value >> 31);
    }

    public int NextInt(int exclusiveMaximum)
    {
        if (exclusiveMaximum <= 0)
            throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));

        var bound = (ulong) exclusiveMaximum;
        var threshold = unchecked((0UL - bound) % bound);
        ulong value;
        do
        {
            value = NextUInt64();
        }
        while (value < threshold);

        return (int) (value % bound);
    }
}
