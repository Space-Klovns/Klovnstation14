namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     Keeps a row's <see cref="KsTguiAnimatedNumber"/> across rebuilds of its list. Windows here rebuild their rows on every
///         state, which would start each number over; tgui keeps them because React matches rows by key. Key by what the row
///         shows, ask for each row's number during a rebuild, and <see cref="Sweep"/> once it is done.
/// </summary>
public sealed class KsTguiAnimatedNumberPool<TKey> where TKey : notnull
{
    private readonly Dictionary<TKey, KsTguiAnimatedNumber> _numbers = new();
    private readonly HashSet<TKey> _usedKeys = new();
    private readonly Func<KsTguiAnimatedNumber> _create;

    public KsTguiAnimatedNumberPool(Func<KsTguiAnimatedNumber> create)
    {
        _create = create;
    }

    /// <summary>
    ///     This key's number, taken out of the row it was in, or a new one the first time the key is seen.
    /// </summary>
    public KsTguiAnimatedNumber Get(TKey key)
    {
        if (!_numbers.TryGetValue(key, out var number))
        {
            number = _create();
            _numbers[key] = number;
        }

        number.Orphan();
        _usedKeys.Add(key);
        return number;
    }

    /// <summary>
    ///     Forgets every number not asked for since the last sweep, so a row that comes back starts over, as a new one.
    /// </summary>
    public void Sweep()
    {
        foreach (var key in _numbers.Keys)
        {
            if (!_usedKeys.Contains(key))
                _numbers.Remove(key);
        }

        _usedKeys.Clear();
    }
}
