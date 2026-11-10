namespace Content.Server._KS14.NPC.Utility;

/// <summary>
///     Keeps the best of the candidates offered to it, by score: the lowest or the highest. For the "remember the
///         nearest so far" loops, without a pair of locals each. Does not allocate.
/// </summary>
/// <typeparam name="T">What is being picked.</typeparam>
public struct NpcBestOf<T>
{
    private readonly bool _lowest;

    /// <summary>
    ///     Whether any candidate has been taken.
    /// </summary>
    public bool Found { get; private set; }

    /// <summary>
    ///     The best candidate so far. Default if none is <see cref="Found"/>.
    /// </summary>
    public T? Value { get; private set; }

    /// <summary>
    ///     The score of the best candidate so far, or what it started at if none is <see cref="Found"/>.
    /// </summary>
    public float Score { get; private set; }

    private NpcBestOf(bool lowest, float startScore)
    {
        _lowest = lowest;
        Score = startScore;
    }

    /// <summary>
    ///     Picks the lowest score. A candidate has to score below <paramref name="startScore"/> to be taken at all.
    /// </summary>
    public static NpcBestOf<T> Lowest(float startScore = float.MaxValue)
    {
        return new NpcBestOf<T>(true, startScore);
    }

    /// <summary>
    ///     Picks the highest score. A candidate has to score above <paramref name="startScore"/> to be taken at all.
    /// </summary>
    public static NpcBestOf<T> Highest(float startScore = float.MinValue)
    {
        return new NpcBestOf<T>(false, startScore);
    }

    /// <summary>
    ///     Takes <paramref name="value"/> if it scores strictly better than the best so far, so of equal scores the
    ///         first offered is kept. Returns whether it was taken.
    /// </summary>
    public bool Offer(T value, float score)
    {
        if (_lowest ? score >= Score : score <= Score)
            return false;

        Take(value, score);
        return true;
    }

    /// <summary>
    ///     As <see cref="Offer"/>, but a score equal to the best so far is taken too, so of equal scores the last
    ///         offered is kept.
    /// </summary>
    public bool OfferPreferLater(T value, float score)
    {
        if (_lowest ? score > Score : score < Score)
            return false;

        Take(value, score);
        return true;
    }

    private void Take(T value, float score)
    {
        Value = value;
        Score = score;
        Found = true;
    }
}
