namespace Content.Shared._KS14.Voting;

/// <summary>
///     Lets a vote's title, initiator and options reach each client in its own language. A vote carries plain strings
///         (<c>VoteOptions</c>, <c>MsgVoteData</c>), which the server would otherwise have to format in its own. A
///         string made with <see cref="Deferred"/> is a localisation id in disguise: the server passes it through
///         untouched, and the client's vote manager turns it into text with <see cref="Resolve"/> as the vote
///         arrives. Any other string, a custom vote's or a player's name, is shown as it is.
/// </summary>
public static class KsVoteText
{
    /// <summary>
    ///     Marks a string as a localisation id. A private-use character: nothing a player types, and nothing a
    ///         translation contains. Written as an escape, as the character itself shows as nothing in an editor.
    /// </summary>
    private const char Marker = '\uE14C';

    /// <summary>
    ///     A localisation id, to be put where a vote wants text: resolved by each client.
    /// </summary>
    public static string Deferred(LocId id)
    {
        return Marker + id.Id;
    }

    /// <summary>
    ///     The text to show for a string a vote carried: the message, in this side's language, if it was made with
    ///         <see cref="Deferred"/>, and the string itself otherwise.
    /// </summary>
    public static string Resolve(string text)
    {
        return text.Length > 1 && text[0] == Marker
            ? Loc.GetString(text[1..])
            : text;
    }

    /// <summary>
    ///     Whether a string was made with <see cref="Deferred"/>, and so is not yet text anyone should read.
    /// </summary>
    public static bool IsDeferred(string text)
    {
        return text.Length > 1 && text[0] == Marker;
    }
}
