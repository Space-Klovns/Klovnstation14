using System.IO;
using System.Linq;

namespace Content.Server._KS14.Procedural;

public enum KsProcgenResourceReferenceKind : byte { Path, Rsi }
public sealed record KsProcgenResourceReference(KsProcgenResourceReferenceKind Kind, string Path, string? State = null);

public sealed record KsProcgenResourceCaptureLimits
{
    public int MaximumReferences { get; init; } = 65_536;
    public int MaximumFiles { get; init; } = 4096;
    public int MaximumFileBytes { get; init; } = 16_777_216;
    public int MaximumTotalBytes { get; init; } = 134_217_728;
    public int MaximumRsiStates { get; init; } = 16_384;
    public int MaximumJsonDepth { get; init; } = 32;
}

/// <summary>Detached content bytes. Reading never reopens a mutable VFS file or exposes the backing array.</summary>
public sealed class KsProcgenCapturedResource
{
    private readonly byte[] _bytes;
    internal KsProcgenCapturedResource(string path, byte[] bytes, string sha256)
    { Path = path; _bytes = bytes; Sha256 = sha256; }
    public string Path { get; }
    public string Sha256 { get; }
    public int ByteCount => _bytes.Length;
    public byte[] CopyBytes() => (byte[]) _bytes.Clone();
    public Stream OpenRead() => new MemoryStream(_bytes, writable: false);
    internal bool SameBytes(KsProcgenCapturedResource other) => Path == other.Path && _bytes.AsSpan().SequenceEqual(other._bytes);
}

internal interface IKsProcgenResourceObservationContext
{
    void ObserveResource(KsProcgenResourceReference reference);
    void ObserveSpriteEntity(string id);
}
