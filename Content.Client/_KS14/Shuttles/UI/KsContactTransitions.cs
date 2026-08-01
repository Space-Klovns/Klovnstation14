using Content.Shared._KS14.Sensors;

namespace Content.Client._KS14.Shuttles.UI;

/// <summary>
///     Client-side liveness edge detector for the presentation anims: remembers when
///         each contact was first seen or flipped live/ghost
///         (<see cref="BecameLive"/>, <see cref="BecameGhost"/>). The wire has no
///         transition timestamps (LastSeen keeps advancing while live, then freezes),
///         so the moment of the flip only exists client-side. Purely cosmetic state:
///         it can only re-time how contacts the console already received are shaded.
/// </summary>
public sealed class KsContactTransitions
{
    private readonly Dictionary<NetEntity, (bool Live, TimeSpan? BecameLive, TimeSpan? BecameGhost)> _seen = new();
    private readonly HashSet<NetEntity> _present = new();
    private readonly List<NetEntity> _stale = new();

    /// <summary>Observes one state push, stamping live/ghost flips; contacts gone from the list are forgotten.</summary>
    public void Update(List<KsSensorContactState>? contacts, TimeSpan now)
    {
        _present.Clear();

        if (contacts != null)
        {
            foreach (var contact in contacts)
            {
                _present.Add(contact.Grid);

                if (!_seen.TryGetValue(contact.Grid, out var prev))
                    _seen[contact.Grid] = (contact.Live, contact.Live ? now : null, null);
                else if (prev.Live != contact.Live)
                    _seen[contact.Grid] = contact.Live
                        ? (true, now, prev.BecameGhost)
                        : (false, prev.BecameLive, now);
            }
        }

        _stale.Clear();
        foreach (var grid in _seen.Keys)
        {
            if (!_present.Contains(grid))
                _stale.Add(grid);
        }

        foreach (var grid in _stale)
        {
            _seen.Remove(grid);
        }
    }

    /// <summary>When the contact last appeared or turned live again; null if it never has.</summary>
    public TimeSpan? BecameLive(NetEntity grid)
        => _seen.TryGetValue(grid, out var state) ? state.BecameLive : null;

    /// <summary>When the contact last turned ghost; null while live or unknown.</summary>
    public TimeSpan? BecameGhost(NetEntity grid)
        => _seen.TryGetValue(grid, out var state) && !state.Live ? state.BecameGhost : null;
}
