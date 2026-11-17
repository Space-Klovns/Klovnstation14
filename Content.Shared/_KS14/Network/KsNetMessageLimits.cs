using System.IO;
using Content.Shared.Administration.Managers;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared._KS14.Network;

/// <summary>
///     Hard limits on what a hand-written <see cref="NetMessage.ReadFromBuffer"/> will read.
/// </summary>
/// <remarks>
///     The engine's <c>AssertAtLeastSize</c> checks a declared length against the bytes actually in the message.
///         That stops a small message from allocating without limit, but a collection is still several times
///         larger in memory than on the wire, and compressed data many times more. These limits are absolute.
///     <para/>
///     Everything here throws, and the engine disconnects whoever sent a message that throws while being read.
///         So a limit that is too low kicks honest players: keep each far above anything real, and keep
///         <c>KsNetMessageLimitsTest</c> comparing them against live content.
/// </remarks>
public static class KsNetMessageLimits
{
    private const int Mebibyte = 1024 * 1024;

    /// <summary>
    ///     Serialised <c>EuiMessageBase</c> sent by an ordinary player.
    /// </summary>
    public const int MaxEuiMessageBytes = Mebibyte;

    /// <summary>
    ///     Serialised <c>EuiMessageBase</c> sent by an admin, or by the server. Deliberately loose: admin windows
    ///         send long text and long lists, and an admin is trusted not to attack the server.
    /// </summary>
    public const int MaxAdminEuiMessageBytes = 64 * Mebibyte;

    /// <summary>
    ///     Serialised <c>EuiStateBase</c>. Only the server sends it, mostly to admin windows that carry long lists
    ///         (logs, notes, bans), so it is deliberately loose.
    /// </summary>
    public const int MaxEuiStateBytes = 128 * Mebibyte;

    /// <summary>
    ///     Serialised character profile, sent by clients.
    /// </summary>
    public const int MaxCharacterProfileBytes = Mebibyte;

    /// <summary>
    ///     Serialised preferences (every character slot) or game settings.
    /// </summary>
    public const int MaxPreferencesBytes = 16 * Mebibyte;

    /// <summary>
    ///     Serialised chat message.
    /// </summary>
    public const int MaxChatMessageBytes = Mebibyte;

    /// <summary>
    ///     Favourite construction recipes, sent by clients. Each is the id of a recipe, so an honest client
    ///         never sends more than there are recipes.
    /// </summary>
    public const int MaxConstructionFavorites = 16384;

    /// <summary>
    ///     Console commands an admin may run. Only sent to admins, so it is deliberately loose.
    /// </summary>
    public const int MaxAdminCommands = 65536;

    /// <summary>
    ///     Entities whose chat messages are deleted at once.
    /// </summary>
    public const int MaxDeletedChatEntities = 65536;

    /// <summary>
    ///     Entries of a list with one entry per prototype of some kind: jobs, antagonists, playtime trackers,
    ///         feedback popups.
    /// </summary>
    public const int MaxPrototypeListEntries = 4096;

    /// <summary>
    ///     Voice previews in one message. There is one per voice, so the server never sends more than there are
    ///         voices.
    /// </summary>
    public const int MaxTtsPreviews = 4096;

    /// <summary>
    ///     Compressed map file. Only sent to admins who are mapping, so it is deliberately loose.
    /// </summary>
    public const int MaxMapDataCompressedBytes = 256 * Mebibyte;

    /// <summary>
    ///     Map file once decompressed. Only sent to admins who are mapping, so it is deliberately loose.
    /// </summary>
    public const int MaxMapDataUncompressedBytes = 512 * Mebibyte;

    /// <summary>
    ///     Throws unless <paramref name="value"/> is between zero and <paramref name="maximum"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the value is negative or over the maximum.</exception>
    public static void AssertAtMost(int value, int maximum)
    {
        if (value < 0 || value > maximum)
            throw new InvalidOperationException($"Net message declares a length of {value}, outside 0 to {maximum}.");
    }

    /// <summary>
    ///     As <see cref="AssertAtMost(int, int)"/>, with a looser <paramref name="adminMaximum"/> for a message
    ///         sent by an admin, or by the server.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the value is negative or over the sender's maximum.</exception>
    public static void AssertAtMost(NetMessage message, int value, int maximum, int adminMaximum)
    {
        // Who sent it is only looked up for a value an ordinary player may not send.
        if (value > maximum && value <= adminMaximum && IsSentByAdminOrServer(message))
            return;

        AssertAtMost(value, maximum);
    }

    private static bool IsSentByAdminOrServer(NetMessage message)
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (message.MsgChannel is not { } channel)
            return false;

        // Read by a client, so sent by the server.
        if (!channel.NetPeer.IsServer)
            return true;

        // A de-adminned admin counts as an ordinary player until they re-admin.
        return IoCManager.Resolve<ISharedPlayerManager>().TryGetSessionByChannel(channel, out var session)
               && IoCManager.Resolve<ISharedAdminManager>().GetAdminData(session) != null;
    }

    /// <summary>
    ///     Throws if <paramref name="message"/> is being read by a server. For messages only a server sends.
    /// </summary>
    /// <remarks>
    ///     Registering a message without a handler does not stop it being read: the server deserialises every
    ///         registered message a client sends, then drops it. Calling this first stops that work being done
    ///         for a message the server would never act on.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown if the message is being read by a server.</exception>
    public static void AssertNotReceivedByServer(NetMessage message)
    {
        // The channel is not set where a message is read outside a real connection, as integration tests do.
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (message.MsgChannel is { } channel && channel.NetPeer.IsServer)
            throw new InvalidOperationException($"{message.GetType().Name} is only sent by the server.");
    }

    /// <summary>
    ///     Copies all of <paramref name="source"/> into <paramref name="destination"/>, throwing as soon as it
    ///         turns out to hold more than <paramref name="maximumBytes"/>.
    /// </summary>
    /// <remarks>
    ///     For decompressing: what a compressed stream expands to is only known by reading it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown if the source holds more than the maximum.</exception>
    public static void CopyAtMost(Stream source, Stream destination, int maximumBytes)
    {
        var chunk = new byte[81920];
        var totalBytes = 0L;

        while (true)
        {
            var readBytes = source.Read(chunk, 0, chunk.Length);
            if (readBytes <= 0)
                return;

            totalBytes += readBytes;
            if (totalBytes > maximumBytes)
                throw new InvalidOperationException($"Net message data is longer than the {maximumBytes} bytes allowed.");

            destination.Write(chunk, 0, readBytes);
        }
    }
}
