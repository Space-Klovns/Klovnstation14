namespace Content.Shared._KS14.Language;

/// <summary>
///     Deterministic seed helpers: a word scrambles identically all round (memorizable
///     vocabulary) and re-rolls next round. FNV-1a because string.GetHashCode is randomized
///     per process.
/// </summary>
public static class KsScrambleRng
{
    public static uint HashWord(ReadOnlySpan<char> word)
    {
        var hash = 2166136261u;
        foreach (var c in word)
        {
            hash ^= char.ToLowerInvariant(c);
            hash *= 16777619u;
        }

        return hash;
    }

    public static ulong Mix(uint wordHash, int roundSeed, int languageSeed)
    {
        var z = wordHash | ((ulong) (uint) roundSeed << 32);
        z ^= (uint) languageSeed * 0x9E3779B97F4A7C15UL;
        return SplitMix(z);
    }

    public static uint Next(ref ulong state)
    {
        state = SplitMix(state);
        return (uint) (state >> 32);
    }

    private static ulong SplitMix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
